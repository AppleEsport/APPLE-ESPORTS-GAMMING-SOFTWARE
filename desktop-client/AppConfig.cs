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

    /// <summary>
    /// "operator" for the counter PC, "user" for a customer-facing gaming PC.
    ///
    /// A user PC is locked down: no close button, no minimise, no Alt+F4, no full-screen
    /// toggle. A customer must not be able to shut the app and reach the Windows desktop,
    /// which is the whole point of a kiosk. Everything that could let them out is behind
    /// <see cref="AdminPin"/>.
    /// </summary>
    public string Role { get; set; } = "operator";

    /// <summary>
    /// PIN that unlocks the protected shortcuts — change server, reconfigure the PC,
    /// un-configure it, or exit a locked kiosk. Without this the shortcuts are inert on a
    /// user PC, because a customer discovering a key combination must not be able to
    /// unbind the machine or escape to the desktop.
    /// </summary>
    public string AdminPin { get; set; } = "";

    /// <summary>Which PC this machine is set up as, e.g. "PC-1". Empty until setup is done.</summary>
    public string PcNumber { get; set; } = "";

    /// <summary>Branch this machine belongs to, as Head Office knows it.</summary>
    public string BranchId { get; set; } = "";

    public string BranchName { get; set; } = "";

    /// <summary>
    /// Set only once Head Office has confirmed the claim. Until then the setup wizard runs
    /// instead of the dashboard — a machine that has not been claimed cannot take customers,
    /// so showing the dashboard would only invite an operator to seat someone at a PC that
    /// will never unlock.
    /// </summary>
    public bool IsSetUp { get; set; }

    public bool IsUserPc => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);

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
            if (!string.IsNullOrWhiteSpace(loaded.Role)) target.Role = loaded.Role.Trim();
            if (loaded.AdminPin is not null) target.AdminPin = loaded.AdminPin;
            if (loaded.PcNumber is not null) target.PcNumber = loaded.PcNumber;
            if (loaded.BranchId is not null) target.BranchId = loaded.BranchId;
            if (loaded.BranchName is not null) target.BranchName = loaded.BranchName;
            if (loaded.IsSetUp) target.IsSetUp = true;
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
