using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppleEsportsErp.ClientAgent.Services;

/// <summary>
/// Keeps the gaming-PC agent itself up to date, with nothing to click and nobody standing at the
/// machine to click it.
///
/// The branch API already solves this for itself: it runs as a Windows service, which is already
/// SYSTEM and has no desktop, so it can replace its own files with no elevation prompt for anyone
/// to miss (see apply-update.ps1's own doc comment). A gaming PC has no service - it is just this
/// one WPF process, running as whichever account is logged in at the counter, which is never an
/// administrator (see AppleEsportsBranch.iss's recommended kiosk account). Asking Windows for
/// elevation here would hit exactly the silent, unanswered UAC prompt that apply-update.ps1 exists
/// to avoid on the counter PC - except a gaming PC has no service to hand the job to instead.
///
/// So this never asks for elevation at all. AppleEsportsBranch.iss grants the logged-in account
/// Modify rights on {app}\agent - and only that one subfolder - while Setup is still elevated, one
/// time, at install. Every check after that is a plain, anonymous HTTP call the branch already
/// exposes for exactly this (<c>/api/releases/agent-latest</c>), and every update is this process
/// downloading a hash-verified exe to its own AppData, renaming its own running file aside, moving
/// the new one into place, and relaunching itself. Nothing here is asked to run as anyone else.
/// </summary>
public class AgentSelfUpdater
{
    private readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleEsportsAgent", "logs", "agent-update.log");

    private readonly string _stageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleEsportsAgent", "updates");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Starts the background check loop. Fire-and-forget by design - see the class comment.</summary>
    public void Start()
    {
        CleanUpPreviousSwap();
        _ = Task.Run(RunLoopAsync);
    }

    /// <summary>
    /// A successful swap on the previous run leaves the old exe behind, renamed aside, because
    /// Windows will not let it be deleted while this process still has it as its running image.
    /// By the next run it is just a leftover file, safe to remove.
    /// </summary>
    private void CleanUpPreviousSwap()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            var oldPath = exePath + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);
        }
        catch { /* Not worth failing startup over a leftover file. */ }
    }

    private async Task RunLoopAsync()
    {
        var interval = TimeSpan.FromSeconds(Math.Max(60, App.AgentConfig.UpdateCheckIntervalSeconds));

        while (true)
        {
            try
            {
                await CheckOnceAsync();
            }
            catch (Exception ex)
            {
                Log($"Update check failed: {ex.Message}");
            }

            await Task.Delay(interval);
        }
    }

    private async Task CheckOnceAsync()
    {
        var baseUrl = App.AgentConfig.OperatorLanUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Log("No OperatorLanUrl configured; cannot ask the branch about updates.");
            return;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        AgentLatestEnvelope? envelope;
        try
        {
            var body = await client.GetStringAsync($"{baseUrl}/api/releases/agent-latest");
            envelope = JsonSerializer.Deserialize<AgentLatestEnvelope>(body, JsonOptions);
        }
        catch (Exception ex)
        {
            // No branch reachable, or the branch itself has no internet to Head Office. Silence
            // here is deliberate and correct: a gaming PC checks constantly, and a shop with the
            // internet down for an hour must not fill a log with the same line every ten minutes.
            Log($"Could not reach {baseUrl}: {ex.GetBaseException().Message}");
            return;
        }

        var data = envelope?.Data;
        if (data is null || !data.Available || string.IsNullOrWhiteSpace(data.Version)
            || string.IsNullOrWhiteSpace(data.Sha256) || string.IsNullOrWhiteSpace(data.DownloadPath))
            return;

        var offered = Version.Parse(data.Version);
        if (offered <= RunningVersion)
            return;

        Log($"Installed {RunningVersion}, offered {offered}. Updating.");

        Directory.CreateDirectory(_stageDir);
        var staged = Path.Combine(_stageDir, $"AppleEsportsAgent-{data.Version}.exe");

        if (!File.Exists(staged) || !await MatchesHashAsync(staged, data.Sha256))
        {
            await using var incoming = await client.GetStreamAsync($"{baseUrl}{data.DownloadPath}");
            var partial = staged + ".part";
            await using (var target = File.Create(partial))
            {
                await incoming.CopyToAsync(target);
            }
            File.Move(partial, staged, overwrite: true);
        }

        // Checked again even for a file just downloaded this second - the same discipline
        // apply-update.ps1 uses for the counter PC. Nothing here is allowed to run unverified.
        if (!await MatchesHashAsync(staged, data.Sha256))
        {
            Log($"HASH MISMATCH for {staged}. Refusing to run it.");
            try { File.Delete(staged); } catch { }
            return;
        }

        SwapInAndRestart(staged);
    }

    private static async Task<bool> MatchesHashAsync(string path, string expectedSha256)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        return actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renames the running exe aside, moves the verified download into its place, relaunches it,
    /// and exits this process.
    ///
    /// Renaming (not deleting) the running file is what makes this possible with no elevation and
    /// no helper process: Windows lets a running exe's directory entry be changed out from under
    /// it - the same trick self-updating browsers rely on - it only refuses to overwrite the bytes
    /// of a mapped image in place. The old file is cleaned up on the next launch, once nothing has
    /// it open any longer (<see cref="CleanUpPreviousSwap"/>).
    /// </summary>
    private void SwapInAndRestart(string newExePath)
    {
        var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(currentExePath))
        {
            Log("Could not determine this process's own exe path. Cannot swap in the update.");
            return;
        }

        try
        {
            var oldPath = currentExePath + ".old";
            if (File.Exists(oldPath)) File.Delete(oldPath);

            File.Move(currentExePath, oldPath);
            File.Move(newExePath, currentExePath);
        }
        catch (Exception ex)
        {
            Log($"Could not swap the update into place: {ex.Message}");
            return;
        }

        Log($"Verified and swapped in. Relaunching {currentExePath}.");

        // Released before the new process starts, or its own single-instance mutex check would
        // lose the race against this process still exiting and report "already running" for good.
        App.ReleaseSingleInstanceMutexForRestart();

        try
        {
            Process.Start(new ProcessStartInfo(currentExePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log($"Swapped in but could not relaunch: {ex.Message}");
        }

        // Not Application.Current.Shutdown(): this call comes from a background Task, off the UI
        // thread, and the update must take effect the moment it is verified rather than wait for
        // whatever the lock screen or dashboard window is doing right now. Every window on this
        // machine closes with the process.
        Environment.Exit(0);
    }

    private static Version RunningVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? new Version(v.Major, v.Minor, v.Build)
            : new Version(0, 0, 0);

    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { /* A log write failing must never take the updater down with it. */ }
    }

    private class AgentLatestEnvelope
    {
        [JsonPropertyName("data")]
        public AgentLatestData? Data { get; set; }
    }

    private class AgentLatestData
    {
        public bool Available { get; set; }
        public string? Version { get; set; }
        public string? Sha256 { get; set; }
        public long SizeBytes { get; set; }
        public string? DownloadPath { get; set; }
    }
}
