using Microsoft.Win32;

namespace AppleEsports.Desktop;

/// <summary>
/// Makes sure this machine comes back on its own, and lets staff deliberately step out.
///
/// Nothing on a gaming PC used to start by itself. A power cut, or a customer choosing Restart,
/// left Windows on its desktop with no lock screen, no member login, no session gate and no
/// billing - the PC was free to use, which defeats the whole kiosk. The installer created a Start
/// Menu and Desktop shortcut and nothing more; the branch API was the only thing that came back,
/// because it was the only Windows service.
///
/// Two halves, because they fail differently. The registry Run entry handles a boot. A scheduled
/// task handles everything else - the app being closed, or crashing at nine in the evening with
/// nobody looking - by checking every couple of minutes. Registered from here rather than only by
/// the installer so that it repairs itself: a Run key removed by a customer, cleanup software or
/// somebody tidying msconfig would otherwise leave the PC quietly unprotected until the next time
/// it was reinstalled, with nothing to indicate it.
/// </summary>
internal static class KioskGuard
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "AppleEsports";
    private const string TaskName = "AppleEsports Kiosk Guard";

    private static string StateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Apple Esports");

    private static string FlagPath => Path.Combine(StateDirectory, "kiosk-off.flag");

    /// <summary>
    /// Which boot this is, as "now minus how long the machine has been up".
    ///
    /// kiosk-guard.ps1 computes this identically ([Environment]::TickCount64), so the two always
    /// agree about which boot they are in - that agreement is what makes "Exit Kiosk Mode lasts
    /// until the next restart" work without needing anywhere to store state that a reboot wipes.
    /// </summary>
    private static string BootStamp =>
        DateTime.Now.AddMilliseconds(-Environment.TickCount64).ToString("yyyy-MM-ddTHH:mm:ss");

    /// <summary>
    /// Called on every launch. Never throws: a PC that cannot register its own guard still has to
    /// run the café, and an unhandled error here would stop the app opening at all - turning a
    /// missing safety net into a machine that does not work.
    /// </summary>
    public static void EnsureRegistered(bool isGamingPc)
    {
        try { EnsureStartsOnBoot(); } catch { /* best effort - see above */ }

        // The relaunch watchdog is for customer machines only. On a counter PC the operator closes
        // the app deliberately, with a PIN, and having it reappear two minutes later would be a
        // fault rather than a feature.
        if (!isGamingPc) return;

        try { EnsureWatchdogTask(); } catch { /* best effort */ }
    }

    private static void EnsureStartsOnBoot()
    {
        if (Environment.ProcessPath is not { } exe) return;

        // HKCU rather than HKLM on purpose: it needs no elevation, so it can be repaired on an
        // ordinary launch instead of only during an install that ran as administrator.
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;

        var wanted = $"\"{exe}\"";
        if (key.GetValue(RunValueName) as string == wanted) return;

        key.SetValue(RunValueName, wanted, RegistryValueKind.String);
    }

    private static void EnsureWatchdogTask()
    {
        if (Environment.ProcessPath is not { } exe) return;

        var script = Path.Combine(Path.GetDirectoryName(exe)!, "kiosk-guard.ps1");
        if (!File.Exists(script)) return;   // older install without the guard shipped

        if (TaskExists()) return;

        // schtasks rather than a scheduled-task COM reference, to keep this to the framework the
        // rest of the client already uses. /RL HIGHEST so it can start the app after a logon where
        // the shell is still coming up; /F to replace a stale definition rather than fail.
        Run("schtasks.exe",
            $"/Create /F /TN \"{TaskName}\" /SC MINUTE /MO 2 /RL HIGHEST " +
            $"/TR \"powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \\\"{script}\\\"\"");
    }

    private static bool TaskExists()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "schtasks.exe", $"/Query /TN \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (p is null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void Run(string fileName, string arguments)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        p?.WaitForExit(15000);
    }

    /// <summary>
    /// "Exit Kiosk Mode / Switch to Windows" - staff have deliberately asked for this PC to be
    /// usable as an ordinary Windows machine, so the watchdog stops putting the app back.
    ///
    /// Stamped with the current boot, which is what makes it end at the next restart. A flag that
    /// outlived a reboot would leave the machine permanently open with nothing on any screen to
    /// say so, and that is a worse state than the bug this whole guard was written to fix.
    /// </summary>
    public static void DisableUntilRestart()
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            File.WriteAllText(FlagPath, BootStamp);
        }
        catch { /* if this fails the watchdog simply reopens the app - the safe direction */ }
    }

    /// <summary>
    /// Clears a flag left behind by an earlier boot.
    ///
    /// The watchdog does this too, but the app can be started by hand before the task's next
    /// two-minute tick, and it should not be looking at a stale flag while it decides anything.
    /// </summary>
    public static void ClearStaleFlag()
    {
        try
        {
            if (!File.Exists(FlagPath)) return;

            var stored = File.ReadAllText(FlagPath).Trim();
            if (!DateTime.TryParse(stored, out var storedBoot)) { File.Delete(FlagPath); return; }
            if (!DateTime.TryParse(BootStamp, out var currentBoot)) return;

            if (Math.Abs((storedBoot - currentBoot).TotalMinutes) >= 3)
                File.Delete(FlagPath);
        }
        catch { /* best effort */ }
    }
}
