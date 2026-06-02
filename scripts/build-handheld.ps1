# Build MerlinInventoryTest.exe (Release, WM6 Professional ARMV4I) and package a .cab.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$proj = Join-Path $repoRoot "handheld-ce\MerlinInventoryTest\MerlinInventoryTest.csproj"
$devenv = "${env:ProgramFiles(x86)}\Microsoft Visual Studio 9.0\Common7\IDE\devenv.com"
$makecab = "${env:ProgramFiles(x86)}\Windows Mobile 6 SDK\Tools\CabWiz\makecab.exe"

if (-not (Test-Path $devenv)) {
    Write-Error "Visual Studio 2008 not found. Install VS2008 Professional + WM6 SDK."
}
if (-not (Test-Path $makecab)) {
    Write-Error "makecab.exe not found. Run scripts\install-handheld-prereqs.ps1 as Administrator."
}

Write-Host "Syncing version..." -ForegroundColor Cyan
& node (Join-Path $repoRoot "scripts\sync-version.js")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building Release (Smart Device)..." -ForegroundColor Cyan
& $devenv $proj /build Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $repoRoot "handheld-ce\MerlinInventoryTest\bin\Release\MerlinInventoryTest.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Build succeeded but exe missing: $exe"
}

$cabDir = Join-Path $repoRoot "handheld-ce\MerlinInventoryTest\cab"
$ddf = Join-Path $cabDir "MerlinInventoryTest.ddf"
$cabExe = Join-Path $cabDir "MerlinInventoryTest.exe"
Copy-Item -Path $exe -Destination $cabExe -Force

Push-Location $cabDir
try {
    Get-ChildItem -Directory | Where-Object { $_.Name -match '^\d+$' } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path "MerlinInventoryTest.cab") { Remove-Item -Force "MerlinInventoryTest.cab" }
    Write-Host "Creating CAB..." -ForegroundColor Cyan
    & $makecab /F MerlinInventoryTest.ddf
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $cabOut = Get-ChildItem -Recurse -Filter "MerlinInventoryTest.cab" | Select-Object -First 1
    if (-not $cabOut) {
        Write-Error "makecab did not produce MerlinInventoryTest.cab"
    }
    Copy-Item $cabOut.FullName (Join-Path $cabDir "MerlinInventoryTest.cab") -Force
}
finally {
    Pop-Location
}

$cabFinal = Join-Path $cabDir "MerlinInventoryTest.cab"
$kb = [math]::Round((Get-Item $cabFinal).Length / 1KB, 1)
Write-Host "Built: $cabFinal ($kb KB)" -ForegroundColor Green

& (Join-Path $repoRoot "scripts\publish-to-deploy.ps1") -CabPath $cabFinal
