<#
    Keeps a gaming PC in kiosk mode, and lets staff deliberately step out of it.

    The problem this solves: nothing on a gaming PC started by itself. A power cut, or a customer
    choosing Restart, left Windows sitting on its desktop with no lock screen, no member login, no
    session gate and no billing - the machine was simply free to use. The shipped installer created
    a Start Menu and a Desktop shortcut and nothing else; only the branch API came back, because
    only the API was a Windows service.

    Run by a scheduled task, both at logon and every couple of minutes, so it covers a boot, a
    power cut, and the app dying part-way through an evening with nobody watching.

    -Resume is "Return to Kiosk Mode": it clears the flag and starts the app again.

    The flag deliberately does not survive a reboot. It records the boot it was written in, and a
    flag from an earlier boot is ignored and deleted. Without that, one "Exit Kiosk Mode" would
    leave the PC unprotected for ever - the machine would come back from every future restart with
    no lock screen and no sign that anything was wrong, which is a far worse failure than the one
    this script exists to fix.
#>

param(
    [switch]$Resume,
    [switch]$Disable
)

$ErrorActionPreference = 'Stop'

$AppName   = 'AppleEsports'
$AgentName = 'AppleEsportsAgent'
$StateDir  = Join-Path $env:ProgramData 'Apple Esports'
$FlagPath  = Join-Path $StateDir 'kiosk-off.flag'

# Both this script and the app work the boot time out the same way - now minus how long the
# machine has been up - so the two always agree on which boot they are in. Compared with a few
# minutes of tolerance rather than exactly, because tick count drifts slightly across sleep and
# clock adjustments and an exact match would call every flag stale.
function Get-BootStamp {
    (Get-Date).AddMilliseconds(-([Environment]::TickCount64)).ToString('yyyy-MM-ddTHH:mm:ss')
}

function Test-FlagIsFromThisBoot {
    if (-not (Test-Path $FlagPath)) { return $false }

    try {
        $stored = [DateTime]::Parse((Get-Content $FlagPath -Raw).Trim())
        $current = [DateTime]::Parse((Get-BootStamp))
        return ([Math]::Abs(($stored - $current).TotalMinutes) -lt 3)
    }
    catch {
        # Unreadable flag is treated as no flag. Erring towards protecting the PC is the whole
        # point; a corrupt file must not be a way to leave a machine open.
        return $false
    }
}

function Get-AppPath {
    # The app lives beside this script - the installer puts both in {app}.
    Join-Path $PSScriptRoot "$AppName.exe"
}

function Start-IfMissing([string]$processName, [string]$path) {
    if (-not (Test-Path $path)) { return }
    if (Get-Process -Name $processName -ErrorAction SilentlyContinue) { return }

    Start-Process -FilePath $path
}

if ($Disable) {
    # Writes the flag that stands the watchdog down until the next restart.
    #
    # Done from here, elevated, rather than by the app itself. The app on a gaming PC runs as
    # whoever is logged in, and this folder was created by the installer as administrator - so the
    # app's own attempt to write the flag was refused and swallowed, and Ctrl+Alt+Q appeared to
    # work while the watchdog put the app straight back two minutes later.
    #
    # It must NOT be somewhere an ordinary user can write. A customer who could create this file
    # could switch the kiosk off themselves and walk out to Windows, which is the whole thing the
    # watchdog exists to prevent. Elevation is the right answer here and costs nothing: a member of
    # staff has just typed the admin PIN and is standing at the machine.
    New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
    Set-Content -Path $FlagPath -Value (Get-BootStamp) -Encoding ascii
    Write-Output 'Kiosk mode off until this PC restarts.'
    exit 0
}

if ($Resume) {
    Remove-Item $FlagPath -Force -ErrorAction SilentlyContinue
    Start-IfMissing -processName $AppName -path (Get-AppPath)
    Write-Output 'Kiosk mode restored.'
    exit 0
}

# Staff stepped out of kiosk mode during this same boot - leave the PC alone, which is the point
# of the option. Any flag older than this boot is stale and gets cleared below.
if (Test-FlagIsFromThisBoot) {
    Write-Output 'Kiosk mode is off until this PC restarts.'
    exit 0
}

Remove-Item $FlagPath -Force -ErrorAction SilentlyContinue

Start-IfMissing -processName $AppName   -path (Get-AppPath)
Start-IfMissing -processName $AgentName -path (Join-Path $PSScriptRoot "$AgentName.exe")
