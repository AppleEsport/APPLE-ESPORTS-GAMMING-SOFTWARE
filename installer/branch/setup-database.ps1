<#
    Sets up the branch's own PostgreSQL, so the shop keeps trading with no internet.

    Run by the installer, once, at the end of a Server Mode install. Safe to run again:
    every step checks whether it has already been done, because a repair or a reinstall
    must never wipe a branch's takings.

    Params come from the installer.
#>
param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [string]$DbName     = 'gamecafe_erp',
    [string]$DbUser     = 'appleesports',
    [int]   $DbPort     = 5433,
    [string]$ServiceName = 'AppleEsportsDb'
)

$ErrorActionPreference = 'Stop'

# Registering a Windows service needs elevation, and the failure deep inside pg_ctl is
# "could not open service manager", which tells whoever is standing there nothing.
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This must run as Administrator - it registers Windows services. The installer does this for you; if you are running it by hand, use an elevated PowerShell."
}

$pgRoot  = Join-Path $InstallDir 'pgsql'
$pgBin   = Join-Path $pgRoot 'bin'

# The database lives in ProgramData, not in Program Files.
#
# Not a preference - PostgreSQL will not run under an administrator account, so initdb
# de-elevates itself by re-executing with a restricted token. That token has the
# Administrators group stripped, and a folder under Program Files is writable only by
# Administrators. initdb therefore dropped its own privileges and could no longer write
# to the directory it had just been told to use, and died there.
#
# ProgramData is also simply where this belongs: Program Files holds binaries and is
# meant to be read-only once installed, while the branch's takings are living data.
$dataRoot = Join-Path $env:ProgramData 'Apple Esports'
$dataDir  = Join-Path $dataRoot 'data'
$logFile  = Join-Path $dataRoot 'logs\postgres.log'

New-Item -ItemType Directory -Force $dataRoot, (Join-Path $dataRoot 'logs'), (Join-Path $dataRoot 'backups') | Out-Null

# The database service and the restricted initdb process both need to write here.
# Users is deliberately included: the restricted token keeps that membership when it
# strips Administrators, and it is what makes initdb able to work at all.
icacls $dataRoot /grant "SYSTEM:(OI)(CI)F" "Administrators:(OI)(CI)F" "Users:(OI)(CI)M" /T /Q 2>&1 | Out-Null

# Everything this script prints is also written to a file. When a branch install goes
# wrong the installer window has already closed, and "it didn't work" with nothing to
# read is the worst place to be standing.
$setupLog = Join-Path $dataRoot 'logs\setup-database.log'
function Write-Step($text) {
    Write-Host "  $text"
    Add-Content $setupLog ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $text)
}
Add-Content $setupLog ("`n=== Database setup started {0:yyyy-MM-dd HH:mm:ss} ===" -f (Get-Date))

try {


    # -- Pick a port that is actually free ---------------------------------------
    # A fixed port is a trap. On a machine running Docker Desktop, 5433 is already
    # forwarded by wslrelay, and initdb would succeed while the service could never
    # bind - leaving an install that reports success and a branch that never starts.
    # An existing install keeps whatever port it already uses, or its connection string
    # would stop matching the running database.
    $existingPortFile = Join-Path $dataRoot 'db.port'

    if (Test-Path $existingPortFile) {
        $DbPort = [int](Get-Content $existingPortFile -Raw).Trim()
        Write-Step "Reusing the port this install already uses ($DbPort)."
    } else {
        $candidate = $DbPort
        while ($candidate -lt ($DbPort + 40)) {
            $inUse = Get-NetTCPConnection -LocalPort $candidate -State Listen -ErrorAction SilentlyContinue
            if (-not $inUse) { break }
            $candidate++
        }
        if ($candidate -ge ($DbPort + 40)) { throw "Could not find a free port near $DbPort for the database." }

        if ($candidate -ne $DbPort) {
            Write-Step "Port $DbPort is already in use on this PC - using $candidate instead."
        }
        $DbPort = $candidate
        Set-Content $existingPortFile $DbPort -NoNewline
    }

    # -- 1. Password -------------------------------------------------------------
    # Generated per machine and never shipped in the installer. A password baked into a
    # build would be identical at all four branches and public the moment one person
    # opened the file.
    $secretFile = Join-Path $dataRoot 'db.secret'

    if (Test-Path $secretFile) {
        $dbPassword = (Get-Content $secretFile -Raw).Trim()
        Write-Step 'Using the existing database password.'
    } else {
        Add-Type -AssemblyName System.Security
        $bytes = New-Object byte[] 24
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $dbPassword = [Convert]::ToBase64String($bytes) -replace '[^A-Za-z0-9]', ''
        Set-Content $secretFile $dbPassword -NoNewline

        # Readable only by SYSTEM and Administrators - the app runs as a service, and no
        # customer-facing account has any reason to see this.
        icacls $secretFile /inheritance:r /grant:r "SYSTEM:(R)" "Administrators:(F)" | Out-Null
        Write-Step 'Generated a database password for this machine.'
    }

    # -- 2. Initialise the data directory ----------------------------------------
    if (Test-Path (Join-Path $dataDir 'PG_VERSION')) {
        Write-Step 'Database already initialised - leaving existing data alone.'
    } else {
        Write-Step 'Initialising the database...'

        # initdb wants to create this itself. An empty folder pre-made by the installer under
        # Program Files inherits ACLs that PostgreSQL refuses to accept for a data directory.
        if (Test-Path $dataDir) { Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue }

        # Somewhere without spaces or Program Files permissions. The password file is read by
        # initdb and deleted immediately afterwards.
        $pwFile = Join-Path $env:SystemDrive 'ae_pw_tmp.txt'
        Set-Content $pwFile $dbPassword -NoNewline -Encoding ASCII

        try {
            # scram-sha-256 rather than trust: the branch PC sits on the same network as
            # every gaming machine, and "no password needed" would apply to all of them.
            #
            # Output is captured, not discarded. It was piped to Out-Null, which threw away
            # the only explanation of a failure and left "Initialising the database..." as
            # the last thing anyone could see.
            $output = & "$pgBin\initdb.exe" -D "$dataDir" -U $DbUser --auth=scram-sha-256 `
                --pwfile="$pwFile" -E UTF8 --no-locale 2>&1
            $code = $LASTEXITCODE

            foreach ($line in $output) {
                Add-Content $setupLog ("    initdb: {0}" -f $line)
            }

            if ($code -ne 0) {
                throw "initdb failed (exit $code). Its output is in this log, just above."
            }
        }
        finally {
            Remove-Item $pwFile -Force -ErrorAction SilentlyContinue
        }
    }

    # -- 3. Local only -----------------------------------------------------------
    # The API is the only thing that should ever talk to this database, and it runs on
    # this same machine. Leaving the port open to the LAN would expose every branch's
    # takings to anything on the cafe network.
    $conf = Join-Path $dataDir 'postgresql.conf'
    $confText = Get-Content $conf -Raw
    $confText = $confText -replace "(?m)^#?listen_addresses\s*=.*", "listen_addresses = 'localhost'"
    $confText = $confText -replace "(?m)^#?port\s*=.*", "port = $DbPort"
    Set-Content $conf $confText -NoNewline

    # -- 4. Windows service ------------------------------------------------------
    # So the shop is ready when the PC boots, rather than when somebody remembers to
    # start something.
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Step 'Database service already registered.'
    } else {
        Write-Step 'Registering the database as a Windows service...'
        & "$pgBin\pg_ctl.exe" register -N $ServiceName -D $dataDir -o "-p $DbPort" -S auto | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not register the database service (exit $LASTEXITCODE)" }
    }

    # Recover by itself after a power cut instead of waiting for someone to notice.
    sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

    Write-Step 'Starting the database...'
    Start-Service $ServiceName
    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Seconds 2
        & "$pgBin\pg_isready.exe" -h localhost -p $DbPort -q
        $ready = ($LASTEXITCODE -eq 0)
    } while (-not $ready -and (Get-Date) -lt $deadline)

    if (-not $ready) { throw "The database did not come up within 60 seconds. See $logFile" }

    # -- 5. Create the database --------------------------------------------------
    $env:PGPASSWORD = $dbPassword
    $exists = & "$pgBin\psql.exe" -h localhost -p $DbPort -U $DbUser -d postgres -tAc `
        "SELECT 1 FROM pg_database WHERE datname='$DbName'"

    if ($exists -eq '1') {
        Write-Step "Database '$DbName' already exists - keeping it."
    } else {
        Write-Step "Creating database '$DbName'..."
        & "$pgBin\createdb.exe" -h localhost -p $DbPort -U $DbUser $DbName
        if ($LASTEXITCODE -ne 0) { throw "Could not create the database (exit $LASTEXITCODE)" }
    }

    # uuid-ossp is required by the schema: several tables default their primary key to
    # uuid_generate_v4(), so migrations fail immediately without it.
    & "$pgBin\psql.exe" -h localhost -p $DbPort -U $DbUser -d $DbName -q -c `
        'CREATE EXTENSION IF NOT EXISTS "uuid-ossp";'
    $env:PGPASSWORD = ''

    # -- 6. Write the API's configuration ----------------------------------------
    # Written here rather than shipped, because it carries secrets generated on this machine.
    $apiSettings = Join-Path $InstallDir 'api\appsettings.Production.json'
    New-Item -ItemType Directory -Force (Split-Path $apiSettings) | Out-Null

    function New-Secret {
        $bytes = New-Object byte[] 48
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }

    # Reuse existing signing keys on a repair or upgrade. Regenerating them would invalidate
    # every token in circulation and sign out the operator mid-shift for no reason.
    $existingConfig = if (Test-Path $apiSettings) { Get-Content $apiSettings -Raw | ConvertFrom-Json } else { $null }
    $jwtSecret  = $existingConfig.Jwt.Secret
    $jwtRefresh = $existingConfig.Jwt.RefreshSecret

    if (-not $jwtSecret)  { $jwtSecret  = New-Secret; Write-Step 'Generated a signing key for this branch.' }
    if (-not $jwtRefresh) { $jwtRefresh = New-Secret }

    # Per machine, never shipped. A key baked into the installer would be identical at all
    # four branches, so a token minted at one would be accepted by the others.
    $apiConfigJson = @{
        ConnectionStrings = @{
            DefaultConnection = "Host=localhost;Port=$DbPort;Database=$DbName;Username=$DbUser;Password=$dbPassword"
        }
        Jwt = @{
            Secret        = $jwtSecret
            RefreshSecret = $jwtRefresh
            Issuer        = 'AppleEsportsErp'
            Audience      = 'AppleEsportsErpClient'
            AccessExpiry  = '24h'
            RefreshExpiry = '7d'
        }
    } | ConvertTo-Json -Depth 5 | Out-String

    # An install from an older build left this file readable-only, even to
    # Administrators. Take ownership back before writing rather than failing.
    if (Test-Path $apiSettings) {
        icacls $apiSettings /grant "Administrators:(F)" 2>&1 | Out-Null
        Remove-Item $apiSettings -Force -ErrorAction SilentlyContinue
    }
    Set-Content $apiSettings $apiConfigJson -NoNewline

    icacls $apiSettings /inheritance:r /grant:r "SYSTEM:(R)" "Administrators:(F)" | Out-Null

    Write-Host ""
    Write-Host "  Database ready on localhost:$DbPort" -ForegroundColor Green
}
catch {
    $message = $_.Exception.Message
    Add-Content $setupLog "[ERROR] $message"
    Add-Content $setupLog $_.InvocationInfo.PositionMessage
    Write-Host "  FAILED: $message" -ForegroundColor Red
    exit 1
}
