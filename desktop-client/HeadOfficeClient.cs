using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AppleEsports.Desktop;

/// <summary>Talks to Head Office during setup, before this machine has any identity.</summary>
public sealed class HeadOfficeClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public HeadOfficeClient(string baseUrl, string gateUser, string gatePassword)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // The dashboard sits behind an nginx Basic Auth gate. The API paths are exempt, but
        // sending it anyway costs nothing and keeps setup working if that ever changes.
        if (!string.IsNullOrEmpty(gateUser))
        {
            var raw = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{gateUser}:{gatePassword}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", raw);
        }
    }

    /// <summary>Is there actually an Apple Esports server at this address?</summary>
    public async Task<(bool Reachable, string Message)> PingAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/api/provisioning/ping");
            if (!response.IsSuccessStatusCode)
                return (false, $"The server answered {(int)response.StatusCode} ({response.ReasonPhrase}).");

            return (true, "Connected to Head Office.");
        }
        catch (TaskCanceledException)
        {
            return (false, "The server did not answer in time. Check the address and the network.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Could not reach the server. {ex.Message}");
        }
    }

    public async Task<List<BranchOption>> GetBranchesAsync()
    {
        var json = await _http.GetStringAsync($"{_baseUrl}/api/provisioning/branches");
        return JsonSerializer.Deserialize<ApiEnvelope<List<BranchOption>>>(json, Json)?.Data ?? new();
    }

    public async Task<BranchProvisioning?> GetBranchAsync(Guid branchId)
    {
        var json = await _http.GetStringAsync($"{_baseUrl}/api/provisioning/branch/{branchId}");
        return JsonSerializer.Deserialize<ApiEnvelope<BranchProvisioning>>(json, Json)?.Data;
    }

    /// <summary>Claims a PC number for this machine. Head Office refuses a second claim.</summary>
    public async Task<(bool Ok, string Message, string? Token, Guid? PcId)> ProvisionAsync(
        Guid branchId, string pcNumber, string machineId)
    {
        var body = JsonSerializer.Serialize(new { branchId, pcNumber, machineId });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{_baseUrl}/api/agent/provision", content);
        var text = await response.Content.ReadAsStringAsync();

        try
        {
            var parsed = JsonSerializer.Deserialize<ApiEnvelope<ProvisionResult>>(text, Json);
            if (response.IsSuccessStatusCode && parsed?.Data is { } ok)
                return (true, ok.Reused ? $"{pcNumber} was already set up on this machine." : $"Set up as {pcNumber}.", ok.Token, ok.PcId);

            // Head Office's refusals are written for staff — pass them through unchanged.
            return (false, parsed?.Error ?? $"Setup failed ({(int)response.StatusCode}).", null, null);
        }
        catch (JsonException)
        {
            return (false, $"Unexpected reply from the server ({(int)response.StatusCode}).", null, null);
        }
    }

    /// <summary>
    /// What branch this address actually is, if it has been set up as exactly one.
    ///
    /// A "user" (gaming PC) role has no branch id of its own to send with a provisioning
    /// claim - only the counter PC across the LAN knows that, from its own adoption. This asks
    /// it, the same way the setup wizard already does for the counter PC itself.
    /// </summary>
    public async Task<(bool Adopted, Guid? BranchId, string? BranchName)> GetIdentityAsync()
    {
        try
        {
            var json = await _http.GetStringAsync($"{_baseUrl}/api/provisioning/identity");
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            var adopted = data.TryGetProperty("adopted", out var a) && a.GetBoolean();
            if (!adopted) return (false, null, null);

            var branches = data.GetProperty("branches");
            if (branches.GetArrayLength() == 0) return (false, null, null);

            var first = branches[0];
            return (true, first.GetProperty("id").GetGuid(), first.GetProperty("name").GetString());
        }
        catch
        {
            return (false, null, null);
        }
    }

    public void Dispose() => _http.Dispose();

    // ── Shapes ──

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Error { get; set; }
    }

    private sealed class ProvisionResult
    {
        public string? Token { get; set; }
        public bool Reused { get; set; }
        public Guid? PcId { get; set; }
    }

    public sealed class BranchOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Address { get; set; }
        public override string ToString() => Name;
    }

    public sealed class BranchProvisioning
    {
        public List<PcOption> Pcs { get; set; } = new();
    }

    public sealed class PcOption
    {
        public Guid Id { get; set; }
        public string PcNumber { get; set; } = "";
        public string? PcName { get; set; }
        public bool IsProvisioned { get; set; }

        // Shown in the dropdown, so a claimed seat is obvious before it is picked rather
        // than failing at the end of setup.
        public override string ToString() =>
            IsProvisioned ? $"{PcNumber}  — already set up on another machine" : PcNumber;
    }
}
