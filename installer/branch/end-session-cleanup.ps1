<#
    Closes whatever a customer opened during their session, the moment it ends - so the next
    customer sits down to a clean desktop, not one still full of the last one's browser tabs,
    Discord, and half-finished game.

    Triggered by MainForm.cs the instant it gets the 'session-ended' message from the overlay
    page (posted on SessionStopped, not the later SessionEnded - play is over the moment the
    time runs out, whether or not billing has been settled yet).

    Deliberately crude rather than tracking PIDs launched during the session: anything with a
    visible window is fair game, on the theory that a locked customer PC has nothing running
    with a window that both (a) the customer put there and (b) needs to survive a session
    boundary. The two things that must never be touched are excluded by name below.

    Usage:  pwsh end-session-cleanup.ps1
#>

$ErrorActionPreference = 'Stop'

# Never kill ourselves, and never kill the Windows shell - explorer.exe gone is a black
# desktop and a very bad time for whoever is standing at this PC next.
$Spared = @('AppleEsports', 'AppleEsportsAgent', 'explorer')

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

if ($closed.Count -gt 0) {
    Write-Host "Closed for the next customer: $($closed -join ', ')"
}
else {
    Write-Host 'Nothing to close - no windowed programs were running.'
}
