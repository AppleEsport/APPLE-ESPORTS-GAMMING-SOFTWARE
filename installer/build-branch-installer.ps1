<#
    Builds AppleEsports-Branch-Setup-<version>.exe — the installer that turns a PC into
    either an operator counter (running the whole branch) or a locked gaming machine.

    Stages everything first so the installer can never ship a stale binary.

    Usage:  pwsh installer\build-branch-installer.ps1
#>

$ErrorActionPreference = 'Stop'

$here     = $PSScriptRoot
$repoRoot = Split-Path $here -Parent
$distDir  = Join-Path $repoRoot 'dist'

function Find-Iscc {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw "Inno Setup not found. Install it with:  winget install --id JRSoftware.InnoSetup"
}

Write-Host "== 1. Staging the branch package ==" -ForegroundColor Cyan
& (Join-Path $here 'branch\build-branch-package.ps1')

Write-Host "`n== 2. Publishing the gaming PC agent ==" -ForegroundColor Cyan
Push-Location (Join-Path $repoRoot 'AppleEsportsErp\src\AppleEsportsErp.ClientAgent')
try {
    dotnet publish -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o publish --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'The agent publish failed' }
}
finally { Pop-Location }

Write-Host "`n== 3. Compiling the installer ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force $distDir | Out-Null
$iscc = Find-Iscc
& $iscc (Join-Path $here 'AppleEsportsBranch.iss') |
    Where-Object { $_ -match 'error|Successful compile' }
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed' }

$setup = Get-ChildItem $distDir -Filter 'AppleEsports-Branch-Setup-*.exe' |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host "`n== Done ==" -ForegroundColor Green
Write-Host ("   {0}  ({1:N0} MB)" -f $setup.Name, ($setup.Length / 1MB))
Write-Host "   $($setup.FullName)"
