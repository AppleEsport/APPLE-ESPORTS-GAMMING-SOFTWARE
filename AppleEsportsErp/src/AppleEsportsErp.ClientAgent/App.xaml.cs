using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;

namespace AppleEsportsErp.ClientAgent;

/// <summary>
/// Single-instance WPF application entry point for the Gaming PC Agent.
/// Reads configuration and launches the lock screen.
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    public static IConfiguration Configuration { get; private set; } = null!;
    public static AgentConfig AgentConfig { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Setting a PIN has to be possible on a machine where the agent is already running, so
        // this runs before the single-instance check. It is the only supported way to produce
        // the value that goes in AdminPinHash — nothing else in this program hashes a PIN, and
        // a PIN typed into the config file in plain text would be readable by the customer.
        if (e.Args.Length >= 1 && e.Args[0].Equals("--hash-pin", StringComparison.OrdinalIgnoreCase))
        {
            HashPinAndExit(e.Args.Length > 1 ? e.Args[1] : null);
            return;
        }

        // Enforce single instance — prevent double-launching
        const string mutexName = "AppleEsportsErp_ClientAgent_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show("Apple Esports Agent is already running.", "Agent", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Load configuration
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.agent.json");
        Configuration = new ConfigurationBuilder()
            .AddJsonFile(configPath, optional: false, reloadOnChange: true)
            .Build();

        AgentConfig = new AgentConfig();
        Configuration.GetSection("Agent").Bind(AgentConfig);

        // Every way out of a locked PC asks this one object. It reads the hash from the config
        // file for now; when the local SQLite database exists it reads pc_identity.admin_pin_hash
        // instead and nothing that calls RequirePin has to change.
        Services.AdminPinService.Current = new Services.AdminPinService(() => AgentConfig.AdminPinHash);

        // Started unconditionally, before the role check below, and independent of the LAN/Cloud
        // hub connection those windows open. A gaming PC with a broken machine token (see
        // DualConnectionService's swallowed auth failure, tracked separately) still needs to be
        // able to update itself - this only ever talks to the branch's plain HTTP releases
        // endpoint, the same one apply-update.ps1 already uses for the counter PC.
        new Services.AgentSelfUpdater().Start();

        // Launch the appropriate window based on role
        if (string.IsNullOrEmpty(AgentConfig.AssignedRole))
        {
            var dashboardWindow = new Views.DashboardWindow("");
            dashboardWindow.Show();
        }
        else if (AgentConfig.AssignedRole == "Operator" || 
                 AgentConfig.AssignedRole == "Admin" || 
                 AgentConfig.AssignedRole == "SuperAdmin")
        {
            var dashboardWindow = new Views.DashboardWindow(AgentConfig.AssignedRole);
            dashboardWindow.Show();
        }
        else // Customer or anything else
        {
            var lockScreen = new Views.LockScreen();
            lockScreen.Show();
        }
    }

    /// <summary>
    /// AppleEsportsAgent.exe --hash-pin 1234
    ///
    /// Prints the hash to paste into AdminPinHash, and puts it on the clipboard — this is a
    /// windowed program with no console, so a dialog is the only place it can be read.
    /// </summary>
    private void HashPinAndExit(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            MessageBox.Show(
                "Usage:  AppleEsportsAgent.exe --hash-pin <pin>\n\n" +
                "Puts the hash on the clipboard. Paste it into Agent:AdminPinHash in " +
                "appsettings.agent.json.",
                "Apple Esports", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var hash = Services.AdminPinService.Hash(pin);

        try { Clipboard.SetText(hash); }
        catch { /* Clipboard can be held by another process — the dialog still shows the hash. */ }

        MessageBox.Show(
            $"Admin PIN hash (copied to the clipboard):\n\n{hash}\n\n" +
            "Paste it into Agent:AdminPinHash in appsettings.agent.json.",
            "Apple Esports", MessageBoxButton.OK, MessageBoxImage.Information);

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { /* already released by ReleaseSingleInstanceMutexForRestart */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Called by <see cref="Services.AgentSelfUpdater"/> immediately before it launches the newly
    /// downloaded exe and exits this process.
    ///
    /// Without this, the mutex is still held for the brief window between starting the new
    /// process and this one actually terminating - and the new process's own single-instance
    /// check (<see cref="OnStartup"/>) would lose that race, report "already running", and quit,
    /// leaving nothing on screen at all until someone manually launches it.
    /// </summary>
    public static void ReleaseSingleInstanceMutexForRestart()
    {
        try { _mutex?.ReleaseMutex(); } catch { /* not held by this process - fine, just being sure */ }
    }
}

/// <summary>Configuration model for the agent</summary>
public class AgentConfig
{
    public string AssignedRole { get; set; } = "";
    public string PcId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string PcNumber { get; set; } = "PC-1";
    public string MachineToken { get; set; } = "";

    /// <summary>
    /// PBKDF2 hash of the admin PIN — never the PIN itself, because this file sits on a machine
    /// the public uses. Empty means no PIN is set, and every escape from the lock screen is then
    /// refused rather than allowed: see <see cref="Services.AdminPinService.RequirePin"/>.
    /// Produced by <c>AppleEsportsAgent.exe --hash-pin &lt;pin&gt;</c>.
    /// </summary>
    public string AdminPinHash { get; set; } = "";
    public string OperatorLanUrl { get; set; } = "http://localhost:5000";
    public string FrontendUrl { get; set; } = "http://localhost:5173";
    public string CloudUrl { get; set; } = "https://api.appleesports.com";
    public int HealthCheckIntervalSeconds { get; set; } = 10;
    public int FailoverThresholdSeconds { get; set; } = 30;

    /// <summary>How often AgentSelfUpdater asks the branch for a newer agent exe.</summary>
    public int UpdateCheckIntervalSeconds { get; set; } = 600;
}
