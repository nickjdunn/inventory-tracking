# Build MerlinStreamTest.exe + CAB (direct server stream test app)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$proj = Join-Path $repoRoot "handheld-ce\MerlinStreamTest\MerlinStreamTest.csproj"
$devenv = "${env:ProgramFiles(x86)}\Microsoft Visual Studio 9.0\Common7\IDE\devenv.com"
$cabwiz = "${env:ProgramFiles(x86)}\Windows Mobile 6 SDK\Tools\CabWiz\Cabwiz.exe"

if (-not (Test-Path $devenv)) {
    Write-Error "Visual Studio 2008 not found."
}
if (-not (Test-Path $cabwiz)) {
    Write-Error "Cabwiz.exe not found."
}

Write-Host "Building MerlinStreamTest Release..." -ForegroundColor Cyan
& $devenv $proj /build Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $repoRoot "handheld-ce\MerlinStreamTest\bin\Release\MerlinStreamTest.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Exe missing: $exe"
}

$cabDir = Join-Path $repoRoot "handheld-ce\MerlinStreamTest\cab"
$inf = Join-Path $cabDir "MerlinStreamTest.inf"
Copy-Item -Path $exe -Destination (Join-Path $cabDir "MerlinStreamTest.exe") -Force

Push-Location $cabDir
try {
    if (Test-Path "MerlinStreamTest.cab") { Remove-Item -Force "MerlinStreamTest.cab" }
    & $cabwiz $inf /dest $cabDir /cpu ARMV4I
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $cabOut = Get-ChildItem -Recurse -Filter "*.cab" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Copy-Item $cabOut.FullName (Join-Path $cabDir "MerlinStreamTest.cab") -Force
}
finally {
    Pop-Location
}

$cabFinal = Join-Path $cabDir "MerlinStreamTest.cab"
$dest = Join-Path $repoRoot "public\deploy\MerlinStreamTest.cab"
New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
Copy-Item $cabFinal $dest -Force
$kb = [math]::Round((Get-Item $dest).Length / 1KB, 1)
Write-Host "Published: $dest ($kb KB)" -ForegroundColor Green
Write-Host "PC viewer: http://<server>:3000/deploy/scanner-stream-test.html" -ForegroundColor Cyan
