<#
    Proves the client points at the branch rather than the cloud, by running the real
    built exe and reading what it actually does - not by reading the source.

    Backs up and restores the real user config, which the migration deliberately rewrites.
#>

$repo    = "c:\Users\harsh\Desktop\FINAL APPLE ESPORTS GAMMING SOFTWARE"
$srcExe  = Join-Path $repo "desktop-client\publish\AppleEsports.exe"
$work    = Join-Path $PSScriptRoot "localfirst"
$userCfg = Join-Path $env:APPDATA "AppleEsports\config.json"
$backup  = Join-Path $PSScriptRoot "config.json.backup"

$pass = 0; $fail = 0
function Check($name, $ok) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  FAIL  $name" -ForegroundColor Red;   $script:fail++ }
}

# The real config is rewritten by the migration under test, so put it back afterwards.
if (Test-Path $userCfg) { Copy-Item $userCfg $backup -Force }

function Restore {
    Get-Process -Name "AppleEsports" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    if (Test-Path $backup) { Copy-Item $backup $userCfg -Force; Remove-Item $backup -Force }
    elseif (Test-Path $userCfg) { Remove-Item $userCfg -Force }
    if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}

try {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Force $work, "$work\api" | Out-Null
    Copy-Item $srcExe $work -Force

    # The marker that says "this is the counter PC": the branch API installed alongside.
    Set-Content "$work\api\AppleEsportsErp.Api.exe" "not a real exe, only the marker" -NoNewline

    Write-Host "`n=== A counter PC still configured for the cloud ===" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force (Split-Path $userCfg) | Out-Null
    @{
        ServerUrl    = "http://140.245.195.222:8081"
        GateUsername = "admin"
        GatePassword = "Admin@123"
        Role         = "operator"
        AdminPin     = "12345"
        IsSetUp      = $true
    } | ConvertTo-Json | Set-Content $userCfg -NoNewline
    Write-Host "  before: $((Get-Content $userCfg -Raw | ConvertFrom-Json).ServerUrl)" -ForegroundColor DarkGray

    $p = Start-Process "$work\AppleEsports.exe" -PassThru
    $deadline = (Get-Date).AddSeconds(40)
    $title = ""
    do {
        Start-Sleep -Seconds 2
        $p.Refresh()
        if (-not $p.HasExited -and $p.MainWindowTitle) { $title = $p.MainWindowTitle }
    } while (-not $title -and (Get-Date) -lt $deadline -and -not $p.HasExited)

    Write-Host "  window title: '$title'" -ForegroundColor DarkGray
    $after = Get-Content $userCfg -Raw | ConvertFrom-Json
    Write-Host "  after : $($after.ServerUrl)" -ForegroundColor DarkGray

    Check 'the saved address was moved to the local branch' ($after.ServerUrl -eq 'http://localhost:5016')
    Check 'the cloud gate username was cleared'             ('' -eq $after.GateUsername)
    Check 'the cloud gate password was cleared'             ('' -eq $after.GatePassword)
    Check 'the admin PIN was preserved'                     ('12345' -eq $after.AdminPin)
    Check 'the PC stayed set up'                            ($true -eq $after.IsSetUp)
    Check 'the title says This PC, not an IP address'       ($title -match 'This PC')
    Check 'no public IP shown in the title'                 ($title -notmatch '140\.245\.195\.222')

    Get-Process -Name "AppleEsports" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    Write-Host "`n=== A gaming PC must keep its counter PC's address ===" -ForegroundColor Cyan
    Remove-Item "$work\api" -Recurse -Force        # no branch API on a gaming PC
    @{
        ServerUrl = "http://192.168.1.50:5016"
        Role      = "user"
        AdminPin  = "12345"
        IsSetUp   = $true
    } | ConvertTo-Json | Set-Content $userCfg -NoNewline

    $p2 = Start-Process "$work\AppleEsports.exe" -PassThru
    Start-Sleep -Seconds 12
    $p2.Refresh()
    $running = -not $p2.HasExited

    $after2 = Get-Content $userCfg -Raw | ConvertFrom-Json
    Write-Host "  after : $($after2.ServerUrl)" -ForegroundColor DarkGray

    Check 'a gaming PC is not repointed at itself' ($after2.ServerUrl -eq 'http://192.168.1.50:5016')
    Check 'the gate credentials stay empty'        ('' -eq "$($after2.GateUsername)$($after2.GatePassword)")
    Check 'it keeps running with its branch down'  $running

    # No title assertion here. A customer PC runs kiosk: borderless, no title bar, kept out
    # of the taskbar so nobody can Alt-Tab out of it. There is no title for anyone to read,
    # and Windows will not report one to another process either - an earlier version of this
    # test failed on that and the fault was the assertion, not the app.
}
finally {
    Restore
}

Write-Host "`n$pass passed, $fail failed" -ForegroundColor $(if ($fail) { 'Red' } else { 'Green' })
exit $(if ($fail) { 1 } else { 0 })
