using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppleEsports.Desktop;

/// <summary>
/// Runtime settings for the desktop client.
///
/// Resolution order (later wins):
///   1. Built-in defaults below
///   2. AppleEsports.config.json sitting next to the .exe  — the deployment default,
///      so a branch can ship a pre-pointed copy without anyone touching the UI
///   3. %APPDATA%\AppleEsports\config.json                 — per-machine override,
///      written when someone changes the server from the Settings dialog
/// </summary>
public sealed class AppConfig
{
    /// <summary>Head Office / branch server the client talks to.</summary>
    public string ServerUrl { get; set; } = "http://140.245.195.222:8081";

    /// <summary>
    /// Credentials for the nginx Basic Auth gate in front of the dashboard.
    /// Left blank means "don't auto-fill" — the browser prompt is shown instead.
    /// </summary>
    public string GateUsername { get; set; } = "";

    public string GatePassword { get; set; } = "";

    public bool StartMaximized { get; set; } = true;

    /// <summary>Hide the window chrome entirely — for gaming PCs running as a kiosk.</summary>
    public bool Kiosk { get; set; } = false;

    // ── Paths ─────────────────────────────────────────────────────────────

    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppleEsports");

    public static string UserConfigPath => Path.Combine(AppDataDirectory, "config.json");

    private static string ExeDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    private static string DeploymentConfigPath =>
        Path.Combine(ExeDirectory, "AppleEsports.config.json");

    /// <summary>WebView2 needs a writable folder for its profile; Program Files is not.</summary>
    public static string WebViewDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleEsports", "WebView2");

    // ── Load / save ───────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static AppConfig Load()
    {
        var config = new AppConfig();
        Merge(config, DeploymentConfigPath);
        Merge(config, UserConfigPath);
        return config;
    }

    private static void Merge(AppConfig target, string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions);
            if (loaded is null) return;

            if (!string.IsNullOrWhiteSpace(loaded.ServerUrl)) target.ServerUrl = loaded.ServerUrl.Trim();
            if (loaded.GateUsername is not null) target.GateUsername = loaded.GateUsername;
            if (loaded.GatePassword is not null) target.GatePassword = loaded.GatePassword;
            target.StartMaximized = loaded.StartMaximized;
            target.Kiosk = loaded.Kiosk;
        }
        catch
        {
            // A malformed config must never stop the app from starting —
            // fall through and keep whatever we already have.
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDirectory);
        File.WriteAllText(UserConfigPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Normalised base address, guaranteed to have a scheme and no trailing slash.</summary>
    public string NormalisedUrl()
    {
        var url = (ServerUrl ?? "").Trim();
        if (url.Length == 0) return "http://140.245.195.222:8081";
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }
        return url.TrimEnd('/');
    }
}
