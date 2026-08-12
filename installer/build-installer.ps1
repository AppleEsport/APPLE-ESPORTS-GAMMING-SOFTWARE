<#
    Builds AppleEsports-Setup-<version>.exe — the file you double-click to install.

    Publishes the desktop client first, then compiles the Inno Setup script around it,
    so the installer can never ship a stale copy of the program.

    Usage:  pwsh installer\build-installer.ps1
#>

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path $PSScriptRoot -Parent
$clientDir  = Join-Path $repoRoot 'desktop-client'
$publishDir = Join-Path $clientDir 'publish'
$distDir    = Join-Path $repoRoot 'dist'
$script     = Join-Path $PSScriptRoot 'AppleEsports.iss'

function Find-Iscc {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    throw "Inno Setup not found. Install it with:  winget install --id JRSoftware.InnoSetup"
}

Write-Host "== 1. Publishing the desktop client ==" -ForegroundColor Cyan
Push-Location $clientDir
try {
    dotnet publish -c Release -o publish --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}
finally { Pop-Location }

$exe = Join-Path $publishDir 'AppleEsports.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe after publish, but it is not there." }
Write-Host ("   AppleEsports.exe  {0:N1} MB" -f ((Get-Item $exe).Length / 1MB))

Write-Host "== 2. Compiling the installer ==" -ForegroundColor Cyan
$iscc = Find-Iscc
New-Item -ItemType Directory -Force $distDir | Out-Null

& $iscc $script | Where-Object { $_ -match 'error|warning|Successful|Compiling' }
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

$setup = Get-ChildItem $distDir -Filter 'AppleEsports-Setup-*.exe' |
         Sort-Object LastWriteTime -Descending |
         Select-Object -First 1

Write-Host ""
Write-Host "== Done ==" -ForegroundColor Green
Write-Host ("   {0}  ({1:N1} MB)" -f $setup.Name, ($setup.Length / 1MB))
Write-Host "   $($setup.FullName)"
