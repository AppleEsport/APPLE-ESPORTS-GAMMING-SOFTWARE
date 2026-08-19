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
    private const string AutoUpdateTaskName = "AppleEsports Auto Update";

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

        // Registered on every machine, both roles. This is what finally makes updates arrive on
        // their own - see EnsureAutoUpdateTask.
        try { EnsureAutoUpdateTask(); } catch { /* best effort */ }

        // The relaunch watchdog is for customer machines only. On a counter PC the operator closes
        // the app deliberately, with a PIN, and having it reappear two minutes later would be a
        // fault rather than a feature.
        if (!isGamingPc) return;

        try { EnsureWatchdogTask(); } catch { /* best effort */ }
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
    /// This is the fix for updates never arriving. The app cannot install one: writing to Program
    /// Files and stopping services needs elevation, so it asked Windows (Verb=runas) and Windows
    /// answered with a prompt. Nobody is at a counter PC at 3am, and nobody at a gaming PC is going
    /// to approve an administrator prompt over their game, so the update simply never happened -
    /// and because Process.Start only fails when a prompt is REFUSED, an unanswered one looked
    /// exactly like having nothing to install. Branches sat four releases behind while the Updates
    /// page said "install updates by themselves" and reported no error at all.
    ///
    /// Counter PCs got away with it because the branch API is a Windows service and could install
    /// on their behalf. Gaming PCs have no service, which is why APPLE144HZ-02 was still on 3.0.6
    /// two days and four releases later.
    ///
    /// /RU SYSTEM is the whole difference: SYSTEM is already privileged and has no desktop to draw a
    /// prompt on, so there is nothing to click and nothing to ignore. The script does its own
    /// downloading and hash checking rather than being handed a file by this app - see the security
    /// note in apply-update.ps1 for why a SYSTEM task must never run a path a user could influence.
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

    private static void EnsureWatchdogTask()
    {
        if (Environment.ProcessPath is not { } exe) return;

        var script = Path.Combine(Path.GetDirectoryName(exe)!, "kiosk-guard.ps1");
        if (!File.Exists(script)) return;   // older install without the guard shipped

        // Rewritten on every launch, not only when the task is missing, and this is the line that
        // actually made the flicker stop.
        //
        // 3.1.4 changed this task to launch through run-hidden.vbs to kill a console window that
        // blinked over customers' games every two minutes. It changed nothing on any real machine,
        // because every one of them already had the task from 3.1.0 - and an existing task was
        // being treated as a correct task, so the code returned on the line above before it could
        // replace anything. The flicker would have outlived every future update. /F replaces, so
        // writing it each time is safe and is the only way a bad definition ever gets corrected.

        // Launched through run-hidden.vbs rather than powershell.exe directly, and that is not
        // fussiness - it is the fix for a console window blinking over a customer's game every two
        // minutes, all evening.
        //
        // This task has to run as the logged-in user: its job is to start the app into that
        // person's session, which SYSTEM cannot do. But Task Scheduler starting powershell.exe
        // interactively creates a console window before PowerShell is running to hide itself, so
        // -WindowStyle Hidden is answered too late and a black box flashes every time. The update
        // task never had this problem because it runs as SYSTEM, which has no desktop at all -
        // which is exactly why this was easy to miss.
        //
        // WScript.Shell.Run with window style 0 never creates a window in the first place.
        var launcher = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "run-hidden.vbs");

        Run("schtasks.exe",
            File.Exists(launcher)
                ? $"/Create /F /TN \"{TaskName}\" /SC MINUTE /MO 2 /RL HIGHEST " +
                  $"/TR \"wscript.exe //B //Nologo \\\"{launcher}\\\" \\\"{script}\\\"\""
                // Older install without the launcher: still register, still works, still blinks.
                : $"/Create /F /TN \"{TaskName}\" /SC MINUTE /MO 2 /RL HIGHEST " +
                  $"/TR \"powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \\\"{script}\\\"\"");
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
        // Written first, in case this process happens to have the rights - a counter PC does, and
        // it saves an elevation prompt there.
        try
        {
            Directory.CreateDirectory(StateDirectory);
            File.WriteAllText(FlagPath, BootStamp);
            if (File.Exists(FlagPath)) return;
        }
        catch { /* fall through to the elevated route below */ }

        // A gaming PC's app runs as whoever is logged in, and this folder belongs to the installer,
        // which created it as administrator. So the write above was simply refused - and being
        // wrapped in a catch, it was refused silently: Ctrl+Alt+Q asked for the PIN, closed the
        // app, and the watchdog put it straight back two minutes later with nothing anywhere to
        // say why.
        //
        // Asking for elevation is the right answer rather than a fallback, and it is not a burden
        // here: somebody has just typed the admin PIN and is standing at the machine. The one thing
        // that must NOT be done is moving the flag somewhere an ordinary user can write - a
        // customer able to create it could switch the kiosk off and walk out to Windows, which is
        // precisely what the watchdog exists to stop.
        try
        {
            var script = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "kiosk-guard.ps1");
            if (!File.Exists(script)) return;

            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{script}\" -Disable",
                UseShellExecute = true,
                Verb = "runas",
            });

            p?.WaitForExit(20000);
        }
        catch
        {
            // Refused at the prompt. The app closes anyway and the watchdog reopens it, which is
            // the safe direction to fail in - a PC that stays protected rather than one that
            // silently does not.
        }
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
