<#
    Frees the files the installer is about to replace.

    Run before any file is copied, on an upgrade over a working branch. The API runs as a
    Windows service and holds its own DLLs open, so replacing them underneath it fails with:

        An error occurred while trying to replace the existing file:
        DeleteFile failed; code 5. Access is denied.

    That happens partway through the copy, leaving a half-upgraded install - some files new,
    some old - which is worse than not having started.

    Never fails the install. If something here cannot be stopped, the copy will hit the same
    lock and Inno will offer its own retry, which is a better place to decide than here.
#>

$ErrorActionPreference = 'SilentlyContinue'

$log = Join-Path $env:ProgramData 'Apple Esports\logs\stop-services.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
Add-Content $log ("`n=== stopping for upgrade {0:yyyy-MM-dd HH:mm:ss} ===" -f (Get-Date))

# The dashboard window first: it is the only one a person is looking at, and on a counter PC
# it holds the WebView2 runtime open underneath it.
Get-Process -Name 'AppleEsports' | ForEach-Object {
    Add-Content $log "  closing dashboard (pid $($_.Id))"
    $_ | Stop-Process -Force
}

# API before database. The API depends on the database, and stopping a service something
# else depends on leaves Windows tearing them down in an order of its own choosing.
foreach ($name in @('AppleEsportsApi', 'AppleEsportsDb')) {
    $svc = Get-Service -Name $name
    if (-not $svc) { Add-Content $log "  $name not installed"; continue }
    if ($svc.Status -eq 'Stopped') { Add-Content $log "  $name already stopped"; continue }

    Add-Content $log "  stopping $name"
    Stop-Service -Name $name -Force

    # Stop-Service returns once Windows accepts the request, which is not the same as the
    # process having exited and released its files.
    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $svc = Get-Service -Name $name
    } while ($svc -and $svc.Status -ne 'Stopped' -and (Get-Date) -lt $deadline)

    Add-Content $log "  $name is now $($svc.Status)"
}

# postgres.exe children can outlive the service briefly and keep the data folder open.
Get-Process -Name 'postgres' |
    Where-Object { $_.Path -like '*Apple Esports*' } |
    ForEach-Object { Add-Content $log "  stopping postgres (pid $($_.Id))"; $_ | Stop-Process -Force }

Start-Sleep -Seconds 2
Add-Content $log "  done"
exit 0
