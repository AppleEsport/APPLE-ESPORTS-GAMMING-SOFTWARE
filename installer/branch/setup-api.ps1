<#
    Registers the branch API as a Windows service and waits until it is actually serving.

    Runs after setup-database.ps1, which writes the connection string this depends on.
    Safe to run again — an existing service is reconfigured rather than duplicated.
#>
param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [int]   $ApiPort     = 5016,
    [string]$ServiceName = 'AppleEsportsApi',
    [string]$DbService   = 'AppleEsportsDb'
)

$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This must run as Administrator — it registers a Windows service. The installer does this for you."
}

# Published assembly name, not a friendly one — renaming it would break the Docker image,
# which launches AppleEsportsErp.Api.dll by name.
$apiExe = Join-Path $InstallDir 'api\AppleEsportsErp.Api.exe'
if (-not (Test-Path $apiExe)) { throw "The API is missing at $apiExe" }

function Write-Step($text) { Write-Host "  $text" }

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Step 'API service already registered — stopping it to update.'
    if ($existing.Status -ne 'Stopped') { Stop-Service $ServiceName -Force }
} else {
    Write-Step 'Registering the API as a Windows service…'
    # binPath quoting matters: the path contains spaces ("Program Files"), and without
    # the inner quotes Windows tries to run "C:\Program" and the service never starts.
    sc.exe create $ServiceName binPath= "\"$apiExe\"" start= auto DisplayName= "Apple Esports API" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not register the API service (exit $LASTEXITCODE)" }
}

# The API cannot do anything without its database, and on a cold boot both start at
# once. Declaring the dependency lets Windows order them instead of the API failing,
# retrying, and filling the log with connection errors every morning.
sc.exe config $ServiceName depend= $DbService | Out-Null

sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
sc.exe description $ServiceName "Runs the Apple Esports branch system. Stopping this stops the branch." | Out-Null

# Bind to every interface: the gaming PCs reach this over the branch LAN, so localhost
# alone would leave them unable to connect.
[Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', "http://0.0.0.0:$ApiPort", 'Machine')
[Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', 'Machine')

Write-Step 'Starting the API…'
Start-Service $ServiceName

# Wait for it to actually answer rather than assume. The service reporting "Running"
# only means the process launched — migrations still have to apply on first start,
# and that is exactly when a fresh install is most likely to fail.
$deadline = (Get-Date).AddSeconds(120)
$ready = $false
do {
    Start-Sleep -Seconds 3
    try {
        $r = Invoke-WebRequest "http://localhost:$ApiPort/api/provisioning/ping" -UseBasicParsing -TimeoutSec 5
        $ready = ($r.StatusCode -eq 200)
    } catch { }
} while (-not $ready -and (Get-Date) -lt $deadline)

if (-not $ready) {
    throw "The API did not start serving within two minutes. Check Event Viewer and $InstallDir\logs."
}

Write-Host ""
Write-Host "  Branch system ready at http://localhost:$ApiPort" -ForegroundColor Green
