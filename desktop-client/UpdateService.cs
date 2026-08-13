using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AppleEsports.Desktop;

/// <summary>
/// Brings a fix from Head Office onto this machine.
///
/// Checks what Head Office has approved, and if it is newer than what is installed here,
/// downloads the installer, <b>verifies it against the hash Head Office published</b>, and
/// runs it silently. The installer upgrades in place — same AppId — so settings and the
/// machine's identity survive.
///
/// The hash check is not a formality. This downloads an executable and runs it with full
/// privileges; branches talk to Head Office over plain HTTP today, so without it anyone able
/// to interfere with the connection could hand all four branches a program of their choosing.
/// A mismatch, or a missing hash, means nothing is run.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly AppConfig _config;
    private readonly HttpClient _http;

    public UpdateService(AppConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };  // installers are large

        if (!string.IsNullOrEmpty(config.GateUsername))
        {
            var raw = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.GateUsername}:{config.GatePassword}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", raw);
        }
    }

    public static Version InstalledVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public sealed record AvailableUpdate(string Version, string ReleaseNotes, string Sha256, long SizeBytes, string DownloadPath);

    /// <summary>
    /// Returns an update only when Head Office has approved one that is genuinely newer.
    /// Never throws: a branch with no internet must carry on serving customers, so a failed
    /// check is simply "nothing to do".
    /// </summary>
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken token = default)
    {
        try
        {
            var url = $"{_config.NormalisedUrl()}/api/releases/latest";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("available", out var available) || !available.GetBoolean()) return null;

            var version = data.GetProperty("version").GetString();
            var sha = data.TryGetProperty("sha256", out var s) ? s.GetString() : null;
            var path = data.TryGetProperty("downloadPath", out var p) ? p.GetString() : null;

            // Refuse to go further without a hash. An unverifiable update is not an update,
            // it is an invitation.
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(sha) || string.IsNullOrWhiteSpace(path))
                return null;

            if (!Version.TryParse(Normalise(version), out var offered)) return null;
            if (offered <= InstalledVersion) return null;   // same or older — nothing to do

            return new AvailableUpdate(
                version,
                data.TryGetProperty("releaseNotes", out var n) ? n.GetString() ?? "" : "",
                sha,
                data.TryGetProperty("sizeBytes", out var b) ? b.GetInt64() : 0,
                path);
        }
        catch
        {
            // Offline, server down, mid-deploy — none of that is this machine's problem.
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer and checks it against the published hash. Returns the path
    /// only if it matches; a mismatched file is deleted rather than left lying around.
    /// </summary>
    public async Task<string?> DownloadAndVerifyAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken token = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "AppleEsportsUpdate");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"AppleEsports-Setup-{update.Version}.exe");

        try
        {
            using (var response = await _http.GetAsync(
                $"{_config.NormalisedUrl()}{update.DownloadPath}", HttpCompletionOption.ResponseHeadersRead, token))
            {
                if (!response.IsSuccessStatusCode) return null;

                var total = response.Content.Headers.ContentLength ?? update.SizeBytes;
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var destination = File.Create(target);

                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), token);
                    copied += read;
                    if (total > 0) progress?.Report((int)(copied * 100 / total));
                }
            }

            string actual;
            await using (var stream = File.OpenRead(target))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
            }

            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                // Corrupted in transit, or tampered with. Either way it does not run.
                TryDelete(target);
                return null;
            }

            return target;
        }
        catch
        {
            TryDelete(target);
            return null;
        }
    }

    /// <summary>
    /// Runs the verified installer and exits so it can replace the running executable.
    ///
    /// /VERYSILENT because nobody is watching a branch PC at 3am, and the installer restarts
    /// the app itself when it finishes.
    /// </summary>
    public static bool Install(string installerPath)
    {
        try
        {
            // /LOG, because without it a failed update leaves nothing to read.
            //
            // An upgrade on a real branch stopped both services, failed partway, rolled the
            // files back and left the shop switched off. Working out why meant Windows event
            // logs and a web server's access log, and even then the installer's own reason was
            // simply gone. One flag would have named the file it could not replace.
            //
            // Written beside the branch's other logs rather than to the temp folder, so it is
            // somewhere a person can be told to look. Overwritten each time: the interesting
            // failure is the one that just happened.
            var log = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Apple Esports", "logs", "update-install.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);

            Process.Start(new ProcessStartInfo(installerPath)
            {
                Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTAPPLICATIONS \"/LOG={log}\"",
                UseShellExecute = true,
                Verb = "runas",   // installing to Program Files needs elevation
            });
            return true;
        }
        catch
        {
            // Refused at the UAC prompt, or blocked by policy. The download is verified and
            // cached, so trying again later costs nothing.
            return false;
        }
    }

    /// <summary>
    /// Pads a marketing-style "2.1" into something Version can compare, so 2.1 and 2.1.0
    /// are not treated as different releases.
    /// </summary>
    private static string Normalise(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => version,
        };
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    public void Dispose() => _http.Dispose();
}
