<#
    Installs updates with nothing to click, on any machine, including gaming PCs.

    Why this exists as a scheduled task rather than inside the app.

    The app cannot install an update on its own. Writing to Program Files and stopping services
    needs elevation, so it asked Windows for it (Verb=runas), and Windows answers by showing a
    prompt. On a counter PC at 3am and on a gaming PC with a customer sitting at it, nobody ever
    answers that prompt - so the update silently never happened, and Process.Start only reports a
    failure if the prompt is actively REFUSED, which meant an unanswered prompt was indistinguishable
    from nothing being available. Branches sat four releases behind with a tick saying "install
    updates by themselves" and no error anywhere. Counter PCs escaped it only because the branch API
    is a Windows service and could install for them; gaming PCs have no service, so they were stuck.

    Run by a task registered as SYSTEM. SYSTEM is already fully privileged and has no desktop to draw
    a prompt on, so there is nothing to click and nothing to swallow it.

    SECURITY - the reason this script does the downloading itself.

    An always-SYSTEM task that runs an executable chosen by someone else is a way to become SYSTEM.
    If the app downloaded the installer and merely told this task where it was, then anyone who could
    write that file or that path - including the customer sitting at a gaming PC - could have their
    own program run as SYSTEM. So nothing here is taken from the app or from any user-writable
    location: the version to install and the hash to expect come from the branch API, the file is
    downloaded by this script into a directory only SYSTEM and Administrators can write, and it is
    checked against the server's hash before it is allowed to run. The config is read from Program
    Files, which needs admin rights to change in the first place.
#>

$ErrorActionPreference = 'Stop'

$AppDir     = $PSScriptRoot
$AppExe     = Join-Path $AppDir 'AppleEsports.exe'
$ConfigPath = Join-Path $AppDir 'AppleEsports.config.json'
$StageDir   = Join-Path $env:ProgramData 'Apple Esports\updates'
$LogPath    = Join-Path $env:ProgramData 'Apple Esports\logs\auto-update.log'

function Write-Log([string]$message) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null
        Add-Content -Path $LogPath -Value $line
    } catch { }
    Write-Output $line
}

try {
    if (-not (Test-Path $AppExe))     { Write-Log 'No AppleEsports.exe beside this script; nothing to update.'; exit 0 }
    if (-not (Test-Path $ConfigPath)) { Write-Log 'No config beside this script; cannot find the branch API.'; exit 0 }

    $installed = [version](Get-Item $AppExe).VersionInfo.FileVersion
    $baseUrl   = ((Get-Content $ConfigPath -Raw | ConvertFrom-Json).ServerUrl).TrimEnd('/')

    if (-not $baseUrl) { Write-Log 'Config has no ServerUrl.'; exit 0 }

    # Asked of this machine's own branch, which proxies Head Office. A gaming PC needs no internet
    # of its own for this - only the shop LAN and a counter PC that is switched on.
    $latest = Invoke-RestMethod -Uri "$baseUrl/api/releases/latest" -TimeoutSec 30
    if (-not $latest.success -or -not $latest.data.available) { exit 0 }

    $offered = [version]$latest.data.version
    if ($offered -le $installed) { exit 0 }

    Write-Log "Installed $installed, offered $offered. Updating."

    # Only SYSTEM and Administrators may write here. This is the line that stops a user-supplied
    # executable ever reaching the installer step below.
    if (-not (Test-Path $StageDir)) {
        New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
        $acl = Get-Acl $StageDir
        $acl.SetAccessRuleProtection($true, $false)   # drop inherited permissions entirely
        foreach ($who in 'NT AUTHORITY\SYSTEM', 'BUILTIN\Administrators') {
            $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
                $who, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
        }
        Set-Acl -Path $StageDir -AclObject $acl
    }

    $target = Join-Path $StageDir ("AppleEsports-Branch-Setup-{0}.exe" -f $latest.data.version)

    # Reuse a file already here and already correct, so a machine that could not install last time
    # does not re-download 164 MB on every attempt.
    $needsDownload = $true
    if (Test-Path $target) {
        if ((Get-FileHash $target -Algorithm SHA256).Hash -eq $latest.data.sha256.ToUpper()) {
            $needsDownload = $false
            Write-Log 'Installer already staged and verified.'
        }
    }

    if ($needsDownload) {
        Write-Log "Downloading from $baseUrl$($latest.data.downloadPath)"
        $progressPreference = 'SilentlyContinue'   # writing progress to a non-console host throws
        Invoke-WebRequest -Uri "$baseUrl$($latest.data.downloadPath)" -OutFile $target -TimeoutSec 1800
    }

    # Checked against what the server published, every time, including for a reused file. A
    # mismatch is where this stops - a SYSTEM task must never run something it cannot vouch for.
    $actual = (Get-FileHash $target -Algorithm SHA256).Hash
    if ($actual -ne $latest.data.sha256.ToUpper()) {
        Write-Log "HASH MISMATCH. Expected $($latest.data.sha256.ToUpper()), got $actual. Refusing to run it."
        Remove-Item $target -Force -ErrorAction SilentlyContinue
        exit 1
    }

    Write-Log "Verified. Installing $offered silently."

    # No -Verb runas: this process is already SYSTEM. That single difference is the whole fix.
    $log = Join-Path $env:ProgramData 'Apple Esports\logs\update-install.log'
    $p = Start-Process -FilePath $target -PassThru -Wait -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/RESTARTAPPLICATIONS', "/LOG=$log")

    Write-Log "Installer exited with $($p.ExitCode)."
    exit $p.ExitCode
}
catch {
    Write-Log "Failed: $($_.Exception.Message)"
    exit 1
}
