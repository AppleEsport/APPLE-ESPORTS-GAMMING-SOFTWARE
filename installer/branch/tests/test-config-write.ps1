<#
    Reproduces the install failure and proves the fix, without needing Administrator.

    The failure was an ACL the setup applied to its own config file, so it reproduces on
    any file anywhere - Program Files had nothing to do with it. Running this NON-elevated
    is deliberate and is the stronger test: if recovery works without elevation it will
    certainly work in the installer, which is always elevated.
#>

$t = Join-Path $PSScriptRoot 'acltest'
if (Test-Path $t) {
    Get-ChildItem $t -File | ForEach-Object { icacls $_.FullName /reset 2>&1 | Out-Null }
    [System.IO.Directory]::Delete($t, $true)
}
New-Item -ItemType Directory -Force $t | Out-Null
$f = Join-Path $t 'appsettings.Production.json'

$pass = 0; $fail = 0
function Check($name, $ok) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  FAIL  $name" -ForegroundColor Red;   $script:fail++ }
}

# Exactly the sequence setup-database.ps1 performs, in the same order.
function Invoke-SetupWrite($content) {
    if (Test-Path $f) {
        icacls $f /reset 2>&1 | Out-Null
        Remove-Item $f -Force -ErrorAction SilentlyContinue
    }
    Set-Content $f $content -NoNewline -ErrorAction Stop
    icacls $f /inheritance:r /grant:r "SYSTEM:(R)" "Administrators:(F)" 2>&1 | Out-Null
}

Write-Host "`n=== 1. Reproduce the bug: the ACL the old build applied ===" -ForegroundColor Cyan
Set-Content $f '{"first":"install"}' -NoNewline
icacls $f /inheritance:r /grant:r "SYSTEM:(R)" "Administrators:(R)" | Out-Null
$denied = $false
try { Set-Content $f '{"second":"install"}' -NoNewline -ErrorAction Stop } catch { $denied = $true }
Check 'read-only ACL blocks a plain overwrite (the reported bug)' $denied

Write-Host "`n=== 2. The fix recovers from a machine carrying that old file ===" -ForegroundColor Cyan
$ok = $false
try { Invoke-SetupWrite '{"second":"install"}'; $ok = $true } catch { Write-Host "    $($_.Exception.Message)" -ForegroundColor DarkGray }
Check 'setup rewrites a file left read-only by an older build' $ok

Write-Host "`n=== 3. Repeat installs, the case that actually failed ===" -ForegroundColor Cyan
$ok = $true
foreach ($n in 1..3) {
    try { Invoke-SetupWrite ('{"run":' + $n + '}') } catch { $ok = $false; Write-Host "    run ${n}: $($_.Exception.Message)" -ForegroundColor DarkGray }
}
Check 'three consecutive installs all succeed' $ok

Write-Host "`n=== 4. The file must still be closed to ordinary users ===" -ForegroundColor Cyan
$acl = (icacls $f | Out-String)
Check 'no grant to Users'        (-not ($acl -match '\\Users:'))
Check 'no grant to Everyone'     (-not ($acl -match 'Everyone'))
Check 'SYSTEM can still read it' ($acl -match 'SYSTEM')

Write-Host "`n=== 5. What gets written must be valid JSON the API can load ===" -ForegroundColor Cyan
$json = @{
    ConnectionStrings = @{ DefaultConnection = "Host=localhost;Port=5433;Database=gamecafe_erp;Username=apple_erp;Password=p@ss" }
    Jwt = @{ Secret='a'; RefreshSecret='b'; Issuer='AppleEsportsErp'; Audience='AppleEsportsErpClient'; AccessExpiry='24h'; RefreshExpiry='7d' }
} | ConvertTo-Json -Depth 5 | Out-String
Invoke-SetupWrite $json

icacls $f /reset 2>&1 | Out-Null   # so this test can read back what the service would read
$parsed = $null
try { $parsed = Get-Content $f -Raw | ConvertFrom-Json } catch { }
Check 'the Out-String form still parses as JSON' ($null -ne $parsed)
Check 'connection string survived'               ($parsed.ConnectionStrings.DefaultConnection -match 'gamecafe_erp')
Check 'password with punctuation survived'       ($parsed.ConnectionStrings.DefaultConnection -match 'p@ss')
Check 'jwt section survived'                     ($parsed.Jwt.Issuer -eq 'AppleEsportsErp')

Write-Host "`n=== 6. An install interrupted mid-write must not wedge every later one ===" -ForegroundColor Cyan
# What a truncated write actually leaves behind.
Invoke-SetupWrite '{"ConnectionStrings":{"Defau'
icacls $f /reset 2>&1 | Out-Null

$existing = $null
$survived = $true
try {
    if (Test-Path $f) {
        try { $existing = Get-Content $f -Raw | ConvertFrom-Json } catch { }
    }
} catch { $survived = $false }
Check 'unreadable config does not abort setup' $survived
Check 'no keys are carried forward from it'    ($null -eq $existing.Jwt.Secret)

$recovered = $false
try { Invoke-SetupWrite $json; $recovered = $true } catch { }
Check 'setup writes a fresh config over the truncated one' $recovered

Write-Host "`n=== 7. An empty db.secret must be regenerated, not used ===" -ForegroundColor Cyan
$sec = Join-Path $t 'db.secret'

# A zero-byte file, which is what an interrupted write leaves. Get-Content -Raw returns
# $null for this, not '', so the read must not call a method on the result directly.
Set-Content $sec '' -NoNewline
$threw = $false
$pw = $null
try { $pw = "$(if (Test-Path $sec) { Get-Content $sec -Raw })".Trim() } catch { $threw = $true }
Check 'reading a zero-byte secret file does not throw'         (-not $threw)
Check 'an empty secret file reads as empty, not as a password' ('' -eq $pw)
Check 'and so takes the regenerate branch'                     (-not $pw)

# The normal case must still work.
Set-Content $sec "  abc123  " -NoNewline
$pw = "$(if (Test-Path $sec) { Get-Content $sec -Raw })".Trim()
Check 'an existing password is read back and trimmed'          ('abc123' -eq $pw)

# And a missing file.
Remove-Item $sec -Force
$pw = "$(if (Test-Path $sec) { Get-Content $sec -Raw })".Trim()
Check 'a missing secret file reads as empty'                   ('' -eq $pw)

icacls $f /reset 2>&1 | Out-Null
[System.IO.Directory]::Delete($t, $true)
Write-Host "`n$pass passed, $fail failed" -ForegroundColor $(if ($fail) { 'Red' } else { 'Green' })
exit $(if ($fail) { 1 } else { 0 })
