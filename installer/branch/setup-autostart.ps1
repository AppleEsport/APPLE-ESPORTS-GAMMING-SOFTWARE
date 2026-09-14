<#
    Makes AppleEsports.exe launch itself the moment this PC's kiosk account logs in - including
    right after a Windows restart, which is what turns restart-on-kill.ps1's "restart the PC"
    into an actual fix rather than just a longer outage. Confirmed before this existed: nothing
    anywhere in this installer put the app anywhere Windows would start it back up on its own -
    a PC that lost power, crashed, or was restarted for any reason needed someone to walk over
    and double-click the desktop icon.

    A logon-triggered Scheduled Task, not a Startup-folder shortcut: this PC is expected to run
    with the kiosk account signed in permanently (auto-logon configured on the machine itself,
    outside this app's own concern), and a task fires reliably the moment that account's
    session actually starts, where a plain shortcut can occasionally lose a race with the
    desktop still finishing initialisation. Run by the installer, already elevated, using
    whichever account is running the installer right now - assumed to be this PC's permanent
    kiosk account, since that is how every kiosk PC here is set up.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$AppExePath
)

$ErrorActionPreference = 'Stop'
$TaskName = 'AppleEsports Auto Start'
$currentUser = "$env:USERDOMAIN\$env:USERNAME"

# /IT (interactive token) is the point: this runs a visible window in the signed-in user's own
# desktop session, not SYSTEM's invisible one - the same Session-0 distinction that is why
# apply-update.ps1 can never launch the app itself either.
& schtasks.exe /Create /F /TN $TaskName /SC ONLOGON /RU $currentUser /IT `
    /TR "`"$AppExePath`"" /RL LIMITED | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Output "Could not register '$TaskName' (schtasks exit $LASTEXITCODE)."
    exit $LASTEXITCODE
}

Write-Output "Registered '$TaskName' for $currentUser."
