<#
    Registers the branch API as a Windows service and waits until it is actually serving.

    Runs after setup-database.ps1, which writes the connection string this depends on.
    Safe to run again - an existing service is reconfigured rather than duplicated.
#>
param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [int]   $ApiPort     = 5016,
    [string]$ServiceName = 'AppleEsportsApi',
    [string]$DbService   = 'AppleEsportsDb'
)

$ErrorActionPreference = 'Stop'

$dataRoot = Join-Path $env:ProgramData 'Apple Esports'
$setupLog = Join-Path $dataRoot 'logs\setup-api.log'
New-Item -ItemType Directory -Force (Split-Path $setupLog) | Out-Null

# Writes to the log as well as the console. The previous version only wrote to the
# console, which the installer discards - so a failure left nothing but a header and no
# way to tell how far it had got.
function Write-Step($text) {
    Write-Host "  $text"
    Add-Content $setupLog ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $text)
}

Add-Content $setupLog ("`n=== API setup started {0:yyyy-MM-dd HH:mm:ss} ===" -f (Get-Date))

# Everything below runs inside a trap so that any failure - not just the ones anticipated -
# ends up in the log with its line number. An unhandled exception previously vanished
# into the installer's hidden window.
try {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
               ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        throw "This must run as Administrator - it registers a Windows service."
    }

    # Published assembly name, not a friendly one - renaming it would break the Docker
    # image, which launches AppleEsportsErp.Api.dll by name.
    $apiExe = Join-Path $InstallDir 'api\AppleEsportsErp.Api.exe'
    if (-not (Test-Path $apiExe)) { throw "The API is missing at $apiExe" }

    $settings = Join-Path $InstallDir 'api\appsettings.Production.json'
    if (-not (Test-Path $settings)) {
        throw "The API has no configuration at $settings. The database setup writes this, so it did not finish."
    }

    # -- Service registration --
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Step 'API service already registered - stopping it to update.'
        if ($existing.Status -ne 'Stopped') {
            Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 3
        }
    } else {
        Write-Step 'Registering the API as a Windows service...'
        # New-Service rather than sc.exe. The path contains spaces ("Program Files"), and
        # getting quotes through PowerShell into sc.exe intact is notoriously fragile -
        # when it goes wrong Windows tries to run "C:\Program" and the service is created
        # but can never start. New-Service handles the quoting itself.
        New-Service -Name $ServiceName `
                    -BinaryPathName "`"$apiExe`"" `
                    -DisplayName 'Apple Esports API' `
                    -Description 'Runs the Apple Esports branch system. Stopping this stops the branch.' `
                    -StartupType Automatic | Out-Null
        Write-Step 'Service registered.'
    }

    # The API cannot do anything without its database, and on a cold boot both start at
    # once. Declaring the dependency lets Windows order them instead of the API failing,
    # retrying, and filling the log with connection errors every morning.
    $r = sc.exe config $ServiceName depend= $DbService 2>&1
    Write-Step "Dependency on $DbService set."

    # Recover by itself after a power cut rather than waiting for someone to notice.
    $r = sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 2>&1

    # Bind to every interface: the gaming PCs reach this over the branch LAN, so localhost
    # alone would leave them unable to connect.
    [Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', "http://0.0.0.0:$ApiPort", 'Machine')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', 'Machine')
    Write-Step "Configured to listen on port $ApiPort."

    Write-Step 'Starting the API...'
    Start-Service $ServiceName -ErrorAction Stop

    # Wait for it to actually answer rather than assume. A service reporting "Running"
    # only means the process launched - migrations still have to apply on first start,
    # and that is exactly when a fresh install is most likely to fail.
    $deadline = (Get-Date).AddSeconds(150)
    $ready = $false
    do {
        Start-Sleep -Seconds 3
        try {
            $resp = Invoke-WebRequest "http://localhost:$ApiPort/api/provisioning/ping" -UseBasicParsing -TimeoutSec 5
            $ready = ($resp.StatusCode -eq 200)
        } catch { }

        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($svc -and $svc.Status -eq 'Stopped') {
            throw "The API service started and then stopped. Check Windows Event Viewer (Application) for AppleEsportsErp.Api."
        }
    } while (-not $ready -and (Get-Date) -lt $deadline)

    if (-not $ready) {
        throw "The API did not answer on port $ApiPort within 150 seconds. It may still be applying database migrations, or it may have failed to start."
    }

    Write-Step "Branch system ready at http://localhost:$ApiPort"
    Write-Host ""
    Write-Host "  Branch system ready at http://localhost:$ApiPort" -ForegroundColor Green
}
catch {
    $message = $_.Exception.Message
    $where   = $_.InvocationInfo.PositionMessage
    Add-Content $setupLog "[ERROR] $message"
    Add-Content $setupLog $where
    Write-Host "  FAILED: $message" -ForegroundColor Red
    exit 1
}
