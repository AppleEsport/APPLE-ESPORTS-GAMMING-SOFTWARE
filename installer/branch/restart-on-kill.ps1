<#
    Restarts the PC if AppleEsports.exe just ended without the app's own permission - a Task
    Manager "End Task" in particular, with nowhere else to write down that it happened.

    Run by a Scheduled Task registered as SYSTEM, triggered the instant Windows logs the app's
    own process termination (Event ID 4689 in the Security log - see -RegisterTrigger below for
    why that specific event, and setup-restart-on-kill.ps1 for how auditing is turned on for
    it). Nothing here runs continuously: this script starts, checks one thing, and exits.

    Why "restart the whole PC" rather than just relaunch the app: a customer who has just
    End-Task'd the app mid-session did it on purpose, almost always to dodge the lock screen or
    the timer. Simply reopening the app hands them exactly what they were trying to get - a
    quiet gap with no lock and no clock. A restart costs them the seconds it takes to reboot,
    the app reopens automatically once Windows is back (the installer's own [Registry] Run
    key, self-repaired on every launch by KioskGuard.EnsureStartsOnBoot - already there
    before this feature, nothing new needed for it), and the
    session itself was never touched: its start and end time are rows in the branch's own
    database, not something this PC was keeping in memory, so the customer is billed exactly
    what they would have been billed anyway.

    WHY THIS APPROACH AND NOT A WATCHDOG PROGRAM
    A watchdog is a program that has to run the whole time, on every PC, forever, to notice
    something rare. This does the opposite: nothing runs at all until the one moment it is
    needed, using only Windows' own built-in event log and Task Scheduler - the same two
    components apply-update.ps1 already relies on. No new process sits in memory, no console
    window can ever appear because there is no window to draw in the first place, and there is
    nothing extra to go wrong with on 14 machines that were not already relying on schtasks.exe
    and shutdown.exe.
#>

$ErrorActionPreference = 'Stop'

$AppDir     = $PSScriptRoot
$MarkerPath = Join-Path $env:ProgramData 'Apple Esports\intentional-exit.marker'
$LogPath    = Join-Path $env:ProgramData 'Apple Esports\logs\restart-on-kill.log'

# A legitimate exit is recent, not merely present - this app can sit closed between customers
# for hours, and an old marker from that last, ordinary close must never be read as permission
# covering a completely different exit much later.
$MaxMarkerAge = [TimeSpan]::FromSeconds(60)

function Write-Log([string]$message) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null
        Add-Content -Path $LogPath -Value $line
    } catch { }
}

try {
    if (Test-Path $MarkerPath) {
        $writtenAt = [DateTimeOffset]::Parse((Get-Content $MarkerPath -Raw).Trim())
        $age = [DateTimeOffset]::UtcNow - $writtenAt

        # Consumed either way: a marker this script has already looked at must never be read
        # again by the next unrelated exit.
        Remove-Item $MarkerPath -Force -ErrorAction SilentlyContinue

        if ($age -le $MaxMarkerAge) {
            Write-Log "Normal exit (marker was $([int]$age.TotalSeconds)s old). Nothing to do."
            exit 0
        }

        Write-Log "Marker found but stale ($([int]$age.TotalSeconds)s old) - treating as unauthorised."
    } else {
        Write-Log 'No exit marker found - the app did not get a chance to run any of its own code.'
    }

    Write-Log 'Restarting the PC.'
    Start-Process -FilePath 'shutdown.exe' -ArgumentList @(
        '/r', '/t', '5', '/c', 'Apple Esports: restarting after the app closed unexpectedly.'
    ) -WindowStyle Hidden
}
catch {
    Write-Log "Failed: $($_.Exception.Message)"
}
