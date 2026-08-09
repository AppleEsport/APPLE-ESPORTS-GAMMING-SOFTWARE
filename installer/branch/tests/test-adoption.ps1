<#
    Proves a fresh branch can take Head Office's identity.

    Runs against a scratch database, never the installed branch or the dev stack, because
    adoption deletes rows. Head Office is the real server - the point is that the two sides
    end up agreeing, so a stub would prove nothing.
#>

$repo = "c:\Users\harsh\Desktop\FINAL APPLE ESPORTS GAMMING SOFTWARE"
$ho   = "http://140.245.195.222:8081"
$db   = "adoption_test"
$port = 5077
$api  = "http://127.0.0.1:$port"

$pass = 0; $fail = 0
function Check($n, $ok, $detail = "") {
    if ($ok) { Write-Host "  PASS  $n" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  FAIL  $n $detail" -ForegroundColor Red; $script:fail++ }
}

# Credentials for the dev postgres in Docker, which is where the scratch database lives.
$env_file = Get-Content "$repo\.env" | Where-Object { $_ -match '^DB_(USER|PASSWORD)=' }
$dbUser = ($env_file | Where-Object { $_ -like 'DB_USER=*' }) -replace '^DB_USER=', ''
$dbPass = ($env_file | Where-Object { $_ -like 'DB_PASSWORD=*' }) -replace '^DB_PASSWORD=', ''

$pg = "$repo\..\..\..\Program Files\Apple Esports\pgsql\bin\psql.exe"
if (-not (Test-Path $pg)) { $pg = "C:\Program Files\Apple Esports\pgsql\bin\psql.exe" }
$env:PGPASSWORD = $dbPass

Write-Host "`n=== scratch database ===" -ForegroundColor Cyan
& $pg -h 127.0.0.1 -p 5433 -U $dbUser -d postgres -c "DROP DATABASE IF EXISTS $db;" 2>&1 | Out-Null
& $pg -h 127.0.0.1 -p 5433 -U $dbUser -d postgres -c "CREATE DATABASE $db;" 2>&1 | Out-Null
$exists = & $pg -h 127.0.0.1 -p 5433 -U $dbUser -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$db';"
Check "created $db" ($exists.Trim() -eq '1')
if ($exists.Trim() -ne '1') { exit 1 }

Write-Host "`n=== starting a branch API against it ===" -ForegroundColor Cyan
$env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=5433;Database=$db;Username=$dbUser;Password=$dbPass"
$env:Sync__HeadOfficeUrl = $ho
$env:Jwt__Secret         = "test-only-secret-value-that-is-long-enough-for-hs256-signing"
$env:Jwt__RefreshSecret  = "test-only-refresh-secret-value-that-is-long-enough-here"
$env:ASPNETCORE_URLS     = $api
$env:ASPNETCORE_ENVIRONMENT = "Production"

# The built DLL directly, not "dotnet run". dotnet run applies Properties/launchSettings.json,
# which sets its own environment and URL and quietly overrode both - the first attempt came up
# in Development on port 5015 while the test waited on 5077.
#
# --urls on the command line rather than the environment variable, because the installer sets
# ASPNETCORE_URLS machine-wide to the real branch port; inheriting that would put this test on
# top of the running branch service.
$dll = "$repo\AppleEsportsErp\src\AppleEsportsErp.Api\bin\Release\net8.0\AppleEsportsErp.Api.dll"
if (-not (Test-Path $dll)) { Write-Host "  build first: $dll not found" -ForegroundColor Red; exit 1 }

$apiLog = Join-Path $PSScriptRoot "adoption-api.log"
Remove-Item $apiLog, "$apiLog.err" -Force -EA SilentlyContinue

$proc = Start-Process "dotnet" `
    -ArgumentList @("`"$dll`"", "--urls", $api) `
    -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $apiLog -RedirectStandardError "$apiLog.err"

$ready = $false
foreach ($i in 1..40) {
    Start-Sleep -Seconds 3
    try { $null = Invoke-RestMethod "$api/api/provisioning/ping" -TimeoutSec 5; $ready = $true; break } catch { }
    if ($proc.HasExited) {
        Write-Host "  API exited early, code $($proc.ExitCode)" -ForegroundColor Red
        Get-Content $apiLog -Tail 15 -EA SilentlyContinue | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        Get-Content "$apiLog.err" -Tail 15 -EA SilentlyContinue | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        break
    }
}
Check "branch API came up (migrations + seed)" $ready
if (-not $ready) { Stop-Process -Id $proc.Id -Force -EA SilentlyContinue; exit 1 }

try {
    Write-Host "`n=== before adoption: a self-invented identity ===" -ForegroundColor Cyan
    $before = (Invoke-RestMethod "$api/api/provisioning/identity" -TimeoutSec 15).data
    Write-Host "  branches here: $($before.branches.Count) -> $($before.branches.name -join ', ')"
    Check "seeded all four branches"      ($before.branches.Count -eq 4)
    Check "reports itself as not adopted" ($before.adopted -eq $false)

    $localAdajan = ($before.branches | Where-Object { $_.name -eq 'Adajan' }).id
    $hoBranches  = (Invoke-RestMethod "$api/api/provisioning/head-office/branches" -TimeoutSec 25).data
    $hoAdajan    = ($hoBranches | Where-Object { $_.name -eq 'Adajan' }).id
    Write-Host "  local  Adajan: $localAdajan"
    Write-Host "  Head Office  : $hoAdajan"
    Check "proxied Head Office's branch list" ($hoBranches.Count -ge 4)
    Check "the two disagree, as reported"     ($localAdajan -ne $hoAdajan)

    Write-Host "`n=== adopting ===" -ForegroundColor Cyan
    $body = @{ branchId = $hoAdajan } | ConvertTo-Json
    $res = (Invoke-RestMethod "$api/api/provisioning/adopt" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 60).data
    Write-Host "  adopted $($res.branchName): $($res.pcs) PCs, $($res.operators) operators, $($res.pricingProfiles) pricing"
    if ($res.operatorsNeedingPassword) { Write-Host "  no local password for: $($res.operatorsNeedingPassword -join ', ')" -ForegroundColor Yellow }

    Check "took Head Office's branch id" ($res.branchId -eq $hoAdajan)
    Check "brought the PCs across"       ($res.pcs -eq 16)
    Check "brought the operators across" ($res.operators -ge 2)

    Write-Host "`n=== after adoption ===" -ForegroundColor Cyan
    $after = (Invoke-RestMethod "$api/api/provisioning/identity" -TimeoutSec 15).data
    Write-Host "  branches here: $($after.branches.Count) -> $($after.branches.name -join ', ')"
    Check "only the branch this PC serves remains" ($after.branches.Count -eq 1)
    Check "and it is Head Office's Adajan"         ($after.branches[0].id -eq $hoAdajan)
    Check "now reports itself adopted"             ($after.adopted -eq $true)

    # The whole point: the ids Head Office validates a session against.
    $hoFull = (Invoke-RestMethod "$ho/api/provisioning/branch/$hoAdajan" -TimeoutSec 25).data
    # Via a file. PowerShell strips the double quotes out of an inline -c argument before
    # psql sees it, and these column names are mixed case, so unquoted they fold to
    # lowercase and the query fails with 'column "id" does not exist'.
    function Query([string]$sql) {
        $f = Join-Path $PSScriptRoot "q.sql"
        Set-Content $f $sql -Encoding ASCII
        $out = & $pg -h 127.0.0.1 -p 5433 -U $dbUser -d $db -tA -f $f
        Remove-Item $f -Force -EA SilentlyContinue
        return @($out | Where-Object { $_ -match '\S' } | ForEach-Object { $_.Trim() })
    }

    $localPcIds = Query 'SELECT "Id" FROM pcs ORDER BY "PcNumber";'
    $hoPcIds = ($hoFull.pcs | Sort-Object pcNumber).id
    $matching = (Compare-Object $localPcIds $hoPcIds -SyncWindow 100).Count -eq 0
    Write-Host "  local PC ids: $($localPcIds.Count), Head Office: $($hoPcIds.Count)"
    Check "every PC id matches Head Office exactly" $matching

    $localOps = Query 'SELECT "Id" FROM operators;'
    $opsMatch = (Compare-Object $localOps ($hoFull.operators.id) -SyncWindow 100).Count -eq 0
    Check "every operator id matches Head Office"   $opsMatch

    Write-Host "`n=== refuses to re-adopt a branch that has traded ===" -ForegroundColor Cyan
    Query @"
INSERT INTO sessions ("Id","BranchId","PcId","OperatorId","StartTime","PlannedDurationMin","GamingType","TotalAmount","GamingAmount","State","CreatedAt","UpdatedAt")
VALUES (gen_random_uuid(),'$hoAdajan','$($hoPcIds[0])','$($hoFull.operators[0].id)',NOW(),60,'standard',0,0,0,NOW(),NOW());
"@ | Out-Null

    $refused = $false; $msg = ""
    try { Invoke-RestMethod "$api/api/provisioning/adopt" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 30 | Out-Null }
    catch {
        $refused = $true
        $sr = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
        $msg = ($sr.ReadToEnd() | ConvertFrom-Json).message
    }
    Write-Host "  $msg" -ForegroundColor DarkGray
    Check "refused once sessions exist" $refused
}
finally {
    # Only this test's process, by id. Matching on name would find the installed branch
    # service too, and stopping that would take the shop down.
    Stop-Process -Id $proc.Id -Force -EA SilentlyContinue
    Start-Sleep -Seconds 2
    & $pg -h 127.0.0.1 -p 5433 -U $dbUser -d postgres -c "DROP DATABASE IF EXISTS $db;" 2>&1 | Out-Null
}

Write-Host "`n$pass passed, $fail failed" -ForegroundColor $(if ($fail) { 'Red' } else { 'Green' })
exit $(if ($fail) { 1 } else { 0 })
