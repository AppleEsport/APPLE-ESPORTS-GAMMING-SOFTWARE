using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Makes a freshly installed branch adopt Head Office's identity.
///
/// Both databases seed themselves independently, so each invents its own GUID for the same
/// physical thing. A branch that sets itself up alone therefore ends up with a private
/// "Adajan" that only exists on that PC — same name, different identity — and everything it
/// reports afterwards names PCs and operators Head Office has never heard of. Nothing looks
/// broken at either end: the shop trades perfectly, and Head Office shows an empty branch.
///
/// The sync inbox already refuses those records, with a message telling whoever reads the
/// log to "provision the branch from Head Office so both sides share identifiers". This is
/// the part that was missing to make that possible.
///
/// Deliberately only for a branch that has not traded yet. Replacing the identity of a
/// machine that already holds sessions and takings would orphan them, so that is refused
/// rather than made to work.
/// </summary>
public sealed class BranchAdoptionService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BranchAdoptionService> _logger;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public BranchAdoptionService(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<BranchAdoptionService> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public string? HeadOfficeUrl => _configuration["Sync:HeadOfficeUrl"]?.TrimEnd('/');

    /// <summary>What this machine currently believes it is.</summary>
    public async Task<object> DescribeIdentityAsync(CancellationToken ct)
    {
        var branches = await _db.Branches.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(ct);

        return new
        {
            headOfficeUrl = HeadOfficeUrl,
            // More than one means this database still holds the four seeded branches and has
            // never been told which one it actually is.
            adopted = branches.Count == 1,
            branches,
            hasTraded = await HasTradedAsync(ct),
        };
    }

    /// <summary>Branches Head Office will let this machine be set up as.</summary>
    public async Task<(bool Ok, string? Error, string? Payload)> ListHeadOfficeBranchesAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(HeadOfficeUrl))
            return (false, "This branch has no Head Office address configured.", null);

        try
        {
            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            var response = await client.GetAsync($"{HeadOfficeUrl}/api/provisioning/branches", ct);

            if (!response.IsSuccessStatusCode)
                return (false, $"Head Office answered {(int)response.StatusCode}.", null);

            return (true, null, await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            // Expected whenever the shop's line is down. Worth naming plainly, because the
            // branch itself is fine and only this one step needs the internet.
            return (false, $"Could not reach Head Office at {HeadOfficeUrl}. {ex.Message}", null);
        }
    }

    /// <summary>
    /// True once anything has been recorded that would be orphaned by changing identity.
    /// </summary>
    private async Task<bool> HasTradedAsync(CancellationToken ct) =>
        await _db.Sessions.AnyAsync(ct) ||
        await _db.Bills.AnyAsync(ct) ||
        await _db.Payments.AnyAsync(ct);

    public async Task<AdoptionResult> AdoptAsync(Guid branchId, bool force, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(HeadOfficeUrl))
            return AdoptionResult.Failed("This branch has no Head Office address configured.");

        if (!force && await HasTradedAsync(ct))
            return AdoptionResult.Failed(
                "This PC has already recorded sessions or takings. Adopting a new identity now " +
                "would leave those records pointing at a branch that no longer exists here.");

        // ── Fetch first, change nothing until it has all arrived ──────────────
        BranchProvisioningPayload? payload;
        try
        {
            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // This machine's own name, so Head Office can tell "the PC that already runs this
            // branch, checking in again" apart from "a second PC trying to become it" - the
            // same distinction BranchHeartbeatController.Receive already draws once a branch is
            // live, moved earlier so it can refuse before a second local database is ever
            // written rather than merely warn after two are already writing.
            var url = $"{HeadOfficeUrl}/api/provisioning/branch/{branchId}" +
                      $"?machineName={Uri.EscapeDataString(Environment.MachineName)}&force={force}";
            var response = await client.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var conflictBody = await response.Content.ReadAsStringAsync(ct);
                return AdoptionResult.Failed(ExtractError(conflictBody)
                    ?? "This branch is already running on another PC.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return AdoptionResult.Failed("Head Office does not have a branch with that id.");

            if (!response.IsSuccessStatusCode)
                return AdoptionResult.Failed($"Head Office answered {(int)response.StatusCode}.");

            var body = await response.Content.ReadAsStringAsync(ct);
            payload = JsonSerializer.Deserialize<ProvisioningEnvelope>(body, Json)?.Data;
        }
        catch (Exception ex)
        {
            return AdoptionResult.Failed($"Could not reach Head Office at {HeadOfficeUrl}. {ex.Message}");
        }

        if (payload?.Branch is null)
            return AdoptionResult.Failed("Head Office returned no branch details.");

        if (payload.Pcs.Count == 0)
            return AdoptionResult.Failed(
                $"Head Office has no PCs recorded for {payload.Branch.Name}. Add them there first, " +
                "so this PC and Head Office agree on which machines exist.");

        // Head Office does not hand out password hashes — that endpoint is unauthenticated,
        // so it must not return anything usable for impersonation. Operators already here
        // under the same username therefore keep the password they already have, and only
        // their identifier changes. Anyone who exists solely at Head Office arrives without a
        // usable password and is reported back, rather than being given one silently.
        var existingLogins = await _db.Operators.AsNoTracking()
            .Select(o => new { o.Username, o.Email, o.PasswordHash, o.MobileNumber })
            .ToListAsync(ct);

        var byUsername = existingLogins
            .GroupBy(o => o.Username.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Order matters: PCs point at pricing profiles, and everything points at a branch.
            _db.Pcs.RemoveRange(await _db.Pcs.ToListAsync(ct));
            _db.PricingProfiles.RemoveRange(await _db.PricingProfiles.ToListAsync(ct));
            _db.Operators.RemoveRange(await _db.Operators.ToListAsync(ct));
            _db.Members.RemoveRange(await _db.Members.ToListAsync(ct));
            await _db.SaveChangesAsync(ct);

            _db.Branches.RemoveRange(await _db.Branches.ToListAsync(ct));
            await _db.SaveChangesAsync(ct);

            var now = DateTimeOffset.UtcNow;

            _db.Branches.Add(new Branch
            {
                Id = payload.Branch.Id,
                Name = payload.Branch.Name,
                Address = payload.Branch.Address,
                OpeningTime = ParseTime(payload.Branch.OpeningTime, new TimeOnly(10, 0)),
                ClosingTime = ParseTime(payload.Branch.ClosingTime, new TimeOnly(2, 0)),
                Status = BranchStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            });

            foreach (var p in payload.PricingProfiles)
            {
                _db.PricingProfiles.Add(new PricingProfile
                {
                    Id = p.Id,
                    Name = p.Name,
                    BaseHourlyRate = p.BaseHourlyRate,
                    BufferMinutes = p.BufferMinutes,
                    BranchId = payload.Branch.Id,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            foreach (var pc in payload.Pcs)
            {
                _db.Pcs.Add(new Pc
                {
                    Id = pc.Id,
                    PcNumber = pc.PcNumber,
                    PcName = pc.PcName,
                    Zone = pc.Zone,
                    MonitorHz = pc.MonitorHz,
                    PricingProfileId = pc.PricingProfileId,
                    BranchId = payload.Branch.Id,
                    // Idle, not AwaitingSetup. Head Office already knows this machine exists;
                    // making the operator claim all sixteen again would be asking a question
                    // that has already been answered.
                    State = PcState.Idle,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            var needPasswords = new List<string>();

            foreach (var op in payload.Operators)
            {
                var match = byUsername.GetValueOrDefault(op.Username.ToLowerInvariant());
                if (match is null) needPasswords.Add(op.Username);

                _db.Operators.Add(new Operator
                {
                    Id = op.Id,
                    FullName = op.FullName,
                    Username = op.Username,
                    Email = match?.Email ?? $"{op.Username}@appleesports.local",
                    // An unmatched operator gets a hash of a value nobody holds, so the
                    // account exists for reporting but cannot be logged into here.
                    PasswordHash = match?.PasswordHash ?? BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                    MobileNumber = match?.MobileNumber,
                    BranchId = payload.Branch.Id,
                    Status = OperatorStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Branch adopted Head Office identity: {Branch} ({BranchId}), {Pcs} PCs, {Operators} operators.",
                payload.Branch.Name, payload.Branch.Id, payload.Pcs.Count, payload.Operators.Count);

            return AdoptionResult.Succeeded(
                payload.Branch.Id, payload.Branch.Name,
                payload.Pcs.Count, payload.Operators.Count, payload.PricingProfiles.Count,
                needPasswords);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Adopting Head Office identity failed; the branch is unchanged.");
            return AdoptionResult.Failed($"Could not apply Head Office's details: {ex.Message}");
        }
    }

    private static TimeOnly ParseTime(string? value, TimeOnly fallback) =>
        TimeOnly.TryParse(value, out var parsed) ? parsed : fallback;

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    // ── Wire shapes ───────────────────────────────────────────────────────────

    private sealed class ProvisioningEnvelope
    {
        public BranchProvisioningPayload? Data { get; set; }
    }

    private sealed class BranchProvisioningPayload
    {
        public BranchDto? Branch { get; set; }
        public List<PcDto> Pcs { get; set; } = new();
        public List<OperatorDto> Operators { get; set; } = new();
        public List<PricingDto> PricingProfiles { get; set; } = new();
    }

    private sealed class BranchDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Address { get; set; }
        public string? OpeningTime { get; set; }
        public string? ClosingTime { get; set; }
    }

    private sealed class PcDto
    {
        public Guid Id { get; set; }
        public string PcNumber { get; set; } = "";
        public string? PcName { get; set; }
        public string? Zone { get; set; }
        public string? MonitorHz { get; set; }
        public Guid? PricingProfileId { get; set; }
    }

    private sealed class OperatorDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = "";
        public string Username { get; set; } = "";
    }

    private sealed class PricingDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public decimal BaseHourlyRate { get; set; }
        public int BufferMinutes { get; set; }
    }
}

public sealed record AdoptionResult(
    bool Success,
    string? Error,
    Guid BranchId,
    string? BranchName,
    int Pcs,
    int Operators,
    int PricingProfiles,
    IReadOnlyList<string> OperatorsNeedingPassword)
{
    public static AdoptionResult Failed(string error) =>
        new(false, error, Guid.Empty, null, 0, 0, 0, Array.Empty<string>());

    public static AdoptionResult Succeeded(
        Guid branchId, string branchName, int pcs, int operators, int pricing,
        IReadOnlyList<string> needPasswords) =>
        new(true, null, branchId, branchName, pcs, operators, pricing, needPasswords);
}
