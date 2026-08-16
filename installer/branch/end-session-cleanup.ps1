<#
    Closes whatever a customer opened during their session, the moment it ends - so the next
    customer sits down to a clean desktop, not one still full of the last one's browser tabs,
    Discord, and half-finished game.

    Triggered by MainForm.cs the instant it gets the 'session-ended' message from the overlay
    page (posted on SessionStopped, not the later SessionEnded - play is over the moment the
    time runs out, whether or not billing has been settled yet).

    Two passes, not one, and browsers are handled separately from everything else. Chrome,
    Edge, Brave, Firefox and friends do not run as a single process - they spawn a whole tree
    of renderer/GPU/utility helper processes that have no window of their own, so a check that
    only looks for "does this process have a visible window" misses most of a browser's own
    process tree and leaves enough of it alive to relaunch itself straight back into the tabs
    it just had open. Known browser executables are therefore killed by name outright first,
    followed by the same generic "anything with a visible window" sweep as before for
    everything else a customer might have opened - games, Discord, Spotify, whatever - since
    there is no way to enumerate every app in advance. A second, identical pass runs a moment
    later to catch anything that was mid-relaunch (a "restore my tabs" child process, a
    crash-recovery dialog) when the first pass ran.

    The two things that must never be touched are excluded by name throughout.

    Usage:  pwsh end-session-cleanup.ps1
#>

$ErrorActionPreference = 'Stop'

# Never kill ourselves, and never kill the Windows shell - explorer.exe gone is a black
# desktop and a very bad time for whoever is standing at this PC next.
$Spared = @('AppleEsports', 'AppleEsportsAgent', 'explorer')

# Killed outright by name, whether or not they currently have a visible window - browsers
# spawn a tree of windowless helper processes the generic sweep below would otherwise miss
# entirely, leaving the browser able to relaunch itself from its own crash-recovery state.
$BrowserNames = @(
    'chrome', 'msedge', 'firefox', 'brave', 'opera', 'opera_gx', 'vivaldi', 'iexplore'
)

function Close-BrowsersByName {
    $closed = @()
    Get-Process -Name $BrowserNames -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $closed += $_.ProcessName
            Stop-Process -Id $_.Id -Force -ErrorAction Stop
        }
        catch {
            # Already gone, or this account can't touch it - the rest of the cleanup must
            # still run.
        }
    }
    return $closed
}

function Close-WindowedProcesses {
    $closed = @()
    Get-Process | Where-Object {
        $_.MainWindowHandle -ne 0 -and
        $_.MainWindowTitle -ne '' -and
        $Spared -notcontains $_.ProcessName
    } | ForEach-Object {
        try {
            $closed += $_.ProcessName
            Stop-Process -Id $_.Id -Force -ErrorAction Stop
        }
        catch {
            # A process that exits on its own between the check and the kill, or one this
            # account has no permission to touch, must not stop the rest of the cleanup.
        }
    }
    return $closed
}

$closed = @()
$closed += Close-BrowsersByName
$closed += Close-WindowedProcesses

Start-Sleep -Milliseconds 800
$closed += Close-BrowsersByName
$closed += Close-WindowedProcesses

$closed = $closed | Select-Object -Unique

if ($closed.Count -gt 0) {
    Write-Host "Closed for the next customer: $($closed -join ', ')"
}
else {
    Write-Host 'Nothing to close - no windowed programs were running.'
}
