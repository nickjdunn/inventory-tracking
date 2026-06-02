# Build MerlinInventoryTest.exe (Release, Windows CE 6 / ARMV4I) and package a CE-installable .cab.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$proj = Join-Path $repoRoot "handheld-ce\MerlinInventoryTest\MerlinInventoryTest.csproj"
$devenv = "${env:ProgramFiles(x86)}\Microsoft Visual Studio 9.0\Common7\IDE\devenv.com"
$cabwiz = "${env:ProgramFiles(x86)}\Windows Mobile 6 SDK\Tools\CabWiz\Cabwiz.exe"

if (-not (Test-Path $devenv)) {
    Write-Error "Visual Studio 2008 not found. Install VS2008 Professional + WM6 SDK."
}
if (-not (Test-Path $cabwiz)) {
    Write-Error "Cabwiz.exe not found. Run scripts\install-handheld-prereqs.ps1 as Administrator."
}

& (Join-Path $PSScriptRoot "Invoke-VersionSync.ps1") -RepoRoot $repoRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building Release (Smart Device)..." -ForegroundColor Cyan
& $devenv $proj /build Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $repoRoot "handheld-ce\MerlinInventoryTest\bin\Release\MerlinInventoryTest.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Build succeeded but exe missing: $exe"
}

$cabDir = Join-Path $repoRoot "handheld-ce\MerlinInventoryTest\cab"
$inf = Join-Path $cabDir "MerlinInventoryTest.inf"
$cabExe = Join-Path $cabDir "MerlinInventoryTest.exe"
Copy-Item -Path $exe -Destination $cabExe -Force

Push-Location $cabDir
try {
    Get-ChildItem -Directory | Where-Object { $_.Name -match '^\d+$' } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path "MerlinInventoryTest.cab") { Remove-Item -Force "MerlinInventoryTest.cab" }
    Write-Host "Creating CE setup CAB (Cabwiz + INF)..." -ForegroundColor Cyan
    & $cabwiz $inf /dest $cabDir /cpu ARMV4I
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $cabOut = Get-ChildItem -Recurse -Filter "*.cab" | Where-Object { $_.Name -like "MerlinInventory*.cab" -or $_.Name -eq "1.cab" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $cabOut) {
        $cabOut = Get-ChildItem -Recurse -Filter "*.cab" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    }
    if (-not $cabOut) {
        Write-Error "Cabwiz did not produce a .cab file"
    }
    Copy-Item $cabOut.FullName (Join-Path $cabDir "MerlinInventoryTest.cab") -Force
}
finally {
    Pop-Location
}

$cabFinal = Join-Path $cabDir "MerlinInventoryTest.cab"
$kb = [math]::Round((Get-Item $cabFinal).Length / 1KB, 1)
Write-Host "Built: $cabFinal ($kb KB)" -ForegroundColor Green
Write-Host "Install on device: tap CAB or run wceload.exe" -ForegroundColor DarkGray

& (Join-Path $repoRoot "scripts\publish-to-deploy.ps1") -CabPath $cabFinal
