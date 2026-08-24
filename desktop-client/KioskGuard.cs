using Microsoft.Win32;

namespace AppleEsports.Desktop;

/// <summary>
/// Makes sure this machine comes back on its own.
///
/// Nothing on a gaming PC used to start by itself. A power cut, or a customer choosing Restart,
/// left Windows on its desktop with no lock screen, no member login, no session gate and no
/// billing - the PC was free to use, which defeats the whole kiosk. The installer created a Start
/// Menu and Desktop shortcut and nothing more; the branch API was the only thing that came back,
/// because it was the only Windows service.
///
/// One piece now, not two. There used to be a second half here - a scheduled task that woke up
/// every two minutes, running a PowerShell script, to relaunch the app if it had been closed or
/// had crashed. That is also what caused a console window to blink over customers' games, and
/// what made "Exit Kiosk Mode" refuse to stay off - the watchdog kept fighting the very thing
/// staff had just asked for. It is gone entirely, script and task both. The tradeoff is explicit
/// and accepted: if the app is closed or crashes mid-session, nothing brings it back until the
/// PC is next restarted. A power cut already meant a restart, which is the case this guard
/// exists for; nothing here was ever meant to survive the app dying while Windows stays up.
///
/// Registered from here rather than only by the installer so that it repairs itself: a Run key
/// removed by a customer, cleanup software or somebody tidying msconfig would otherwise leave
/// the PC quietly unprotected until the next time it was reinstalled, with nothing to indicate it.
/// </summary>
internal static class KioskGuard
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "AppleEsports";
    private const string AutoUpdateTaskName = "AppleEsports Auto Update";

    /// <summary>The watchdog task this version retires. Only referenced now to remove it.</summary>
    private const string LegacyWatchdogTaskName = "AppleEsports Kiosk Guard";

    /// <summary>
    /// Called on every launch. Never throws: a PC that cannot register its own guard still has to
    /// run the café, and an unhandled error here would stop the app opening at all - turning a
    /// missing safety net into a machine that does not work.
    /// </summary>
    public static void EnsureRegistered(bool isGamingPc)
    {
        try { EnsureStartsOnBoot(); } catch { /* best effort - see above */ }

        // Registered on every machine, both roles. This is what finally makes updates arrive on
        // their own - see EnsureAutoUpdateTask.
        try { EnsureAutoUpdateTask(); } catch { /* best effort */ }

        // A machine upgraded from an older build may still have the retired watchdog task and its
        // now-deleted script files registered. Left alone, Task Scheduler would keep trying to
        // launch a script that no longer exists, every two minutes, forever. Harmless to call when
        // there is nothing to remove - schtasks simply reports so.
        if (isGamingPc) try { RemoveLegacyWatchdogTask(); } catch { /* best effort */ }
    }

    /// <summary>
    /// Whether a SYSTEM task is installing updates for this machine, in which case this app must
    /// not try to do it itself.
    ///
    /// It would only raise a prompt it cannot answer, and on a gaming PC that prompt appears in
    /// front of a customer - a Windows box asking for an administrator over the top of their game,
    /// which is worse than the missing update it is trying to fix.
    /// </summary>
    public static bool AutoUpdateTaskInstalled() => TaskExists(AutoUpdateTaskName);

    /// <summary>
    /// The task that actually installs updates, running as SYSTEM.
    ///
    /// This call has been silently failing on every ordinary launch since the day it was written,
    /// on a counter PC as much as a gaming PC, and it is worth being honest about why rather than
    /// leaving the older, more hopeful version of this comment in place.
    ///
    /// Creating a task that runs /RU SYSTEM /RL HIGHEST needs an elevated caller. This app runs as
    /// whoever is logged in - not elevated, even on an administrator's own account, because
    /// Windows does not elevate an app just because the signed-in user could approve a prompt. So
    /// this has only ever actually succeeded once per machine: immediately after install, when it
    /// happened to inherit the installer's own elevation for that one launch - at which point the
    /// installer's own [Run] step had already registered the same task moments earlier anyway,
    /// making this call redundant even in the one case where it worked.
    ///
    /// On a counter PC this no longer matters: AutoUpdateTaskGuard on the branch API repairs the
    /// same task on every service startup instead, and a Windows service is always elevated and
    /// always running, which this app is neither. That is the real fix for "the Updates page says
    /// one is waiting, but it never installs" - a task disturbed by a Windows Update, security
    /// software, or someone tidying Task Scheduler by hand could never be recreated by anything on
    /// the machine short of a full reinstall, and this call was never able to do it either.
    ///
    /// A gaming PC has no local service, so it has nothing else and this stays exactly as
    /// unreliable there as it always was - a real gap, not yet closed.
    /// </summary>
    private static void EnsureAutoUpdateTask()
    {
        if (Environment.ProcessPath is not { } exe) return;

        var script = Path.Combine(Path.GetDirectoryName(exe)!, "apply-update.ps1");
        if (!File.Exists(script)) return;   // older install without the updater shipped

        // Written every launch, not only when missing. /F replaces, so this is idempotent.
        //
        // Skipping when a task already exists is what let a broken definition live for ever: an
        // existing task was taken as a correct one. A machine that received a task from an older
        // build kept that build's command line through every update after it, because the code that
        // would have corrected it returned early on the first line. Two schtasks calls on a
        // background thread at startup is nothing; a definition that can never be repaired is not.

        // Every 15 minutes. Frequent enough that a release reaches the shop the same day without
        // anybody thinking about it, and rare enough that thirty-five gaming PCs asking their
        // counter for the latest version is nothing next to one of them downloading it.
        Run("schtasks.exe",
            $"/Create /F /TN \"{AutoUpdateTaskName}\" /SC MINUTE /MO 15 /RU SYSTEM /RL HIGHEST " +
            $"/TR \"powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \\\"{script}\\\"\"");
    }

    private static void EnsureStartsOnBoot()
    {
        if (Environment.ProcessPath is not { } exe) return;

        var wanted = $"\"{exe}\"";

        // The installer's machine-wide entry is the real one - it starts the app for whoever logs
        // in, which is what a shared machine needs. If it is there and correct, leave it alone.
        //
        // Checked before writing anything rather than writing regardless, because there is no
        // single-instance lock stopping two Run entries from opening two copies of a kiosk overlay
        // at once - and two full-screen lock screens fighting over the same seat is its own bug.
        // Program.cs now holds a mutex as the backstop, but not creating the duplicate is better
        // than surviving it.
        try
        {
            using var machine = Registry.LocalMachine.OpenSubKey(RunKey, writable: false);
            if (machine?.GetValue(RunValueName) as string == wanted) return;
        }
        catch { /* unreadable HKLM - fall through and register per-user */ }

        // Per-user fallback, for a machine whose HKLM entry was removed or never written. Needs no
        // elevation, so an ordinary launch can repair it; correct here because this process is
        // running as the person actually logged in, which is exactly what the installer was not.
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;

        if (key.GetValue(RunValueName) as string == wanted) return;

        key.SetValue(RunValueName, wanted, RegistryValueKind.String);
    }

    /// <summary>
    /// Removes the watchdog task this version retires, from any machine that still has it from
    /// an older install. /Delete on a task that does not exist just fails quietly - nothing here
    /// needs to know in advance whether there was anything to remove.
    /// </summary>
    private static void RemoveLegacyWatchdogTask()
    {
        Run("schtasks.exe", $"/Delete /F /TN \"{LegacyWatchdogTaskName}\"");
    }

    private static bool TaskExists(string taskName)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "schtasks.exe", $"/Query /TN \"{taskName}\"")
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
}
