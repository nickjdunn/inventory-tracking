# Checks whether this PC can build the Windows CE handheld app.
Write-Host "Merlin handheld build environment check" -ForegroundColor Cyan
Write-Host ""

$ok = 0
$need = 0

function Test-ItemReport {
    param([string]$Label, [string]$Path, [string]$Hint = "")
    if (Test-Path $Path) {
        Write-Host "[OK] $Label" -ForegroundColor Green
        Write-Host "     $Path" -ForegroundColor DarkGray
        $script:ok++
        return $true
    }
    Write-Host "[--] $Label" -ForegroundColor Yellow
    if ($Hint) { Write-Host "     $Hint" -ForegroundColor Yellow }
    $script:need++
    return $false
}

$vsDevenv = "${env:ProgramFiles(x86)}\Microsoft Visual Studio 9.0\Common7\IDE\devenv.exe"
$null = Test-ItemReport "Visual Studio 2008" $vsDevenv "Install VS2008 Professional (Smart Device is not in Standard/Express)."

$vsSku = (Get-ItemProperty "HKLM:\SOFTWARE\Wow6432Node\Microsoft\VisualStudio\9.0\Setup\VS" -ErrorAction SilentlyContinue).ProductName
$vsDir = Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio 9.0" -Directory -Filter "Microsoft Visual Studio 2008*" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $vsSku -and $vsDir) { $vsSku = $vsDir.Name }
if ($vsSku) {
    if ($vsSku -match "Professional|Team|Enterprise") {
        Write-Host "[OK] Edition supports Smart Device: $vsSku" -ForegroundColor Green
    } elseif ($vsSku -match "Standard|Express") {
        Write-Host "[!!] Edition blocks CE builds: $vsSku" -ForegroundColor Red
        Write-Host "     Smart Device requires VS2008 Professional or higher (not Standard)." -ForegroundColor Yellow
    } else {
        Write-Host "[--] VS edition: $vsSku" -ForegroundColor Yellow
    }
}

$cfSdk = "${env:ProgramFiles(x86)}\Microsoft.NET\SDK\CompactFramework\v3.5\WindowsCE"
if (Test-Path $cfSdk) {
    Write-Host "[OK] .NET Compact Framework 3.5 SDK files" -ForegroundColor Green
    Write-Host "     $cfSdk" -ForegroundColor DarkGray
} else {
    Write-Host "[--] .NET Compact Framework 3.5 SDK files" -ForegroundColor Yellow
}

$cfTargets = @(
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio 9.0\MSBuild\Microsoft\CompactFramework\v3.5\Microsoft.CompactFramework.CSharp.targets",
    "${env:ProgramFiles(x86)}\Microsoft.NET\SDK\CompactFramework\v3.5\Microsoft.CompactFramework.CSharp.targets",
    "${env:ProgramFiles(x86)}\Windows\Microsoft.NET\Framework\v3.5\Microsoft.CompactFramework.CSharp.targets"
)
$hasCfTargets = $false
foreach ($t in $cfTargets) {
    if (Test-Path $t) { $hasCfTargets = $true; break }
}
if ($hasCfTargets) {
    Write-Host "[OK] .NET Compact Framework 3.5 build targets" -ForegroundColor Green
    Write-Host "     $t" -ForegroundColor DarkGray
    $ok++
} else {
    Write-Host "[--] .NET Compact Framework 3.5 build targets (required)" -ForegroundColor Yellow
    Write-Host "     Install: .NET Compact Framework 3.5 + SDK / developer pack" -ForegroundColor Yellow
    Write-Host "     Then: Windows Mobile 6 Professional SDK" -ForegroundColor Yellow
    $need++
}

$smartDll = "${env:ProgramFiles(x86)}\Microsoft Visual Studio 9.0\VC#\VCSPackages\VCSharpSmartDeviceProject.dll"
$null = Test-ItemReport "Smart Device project support (VS)" $smartDll "In VS2008 Setup, enable Smart Device Programmability; install WM6 SDK."

$wmSdk = "${env:ProgramFiles(x86)}\Windows Mobile 6 SDK"
if (-not (Test-Path $wmSdk)) {
    Write-Host "[--] Windows Mobile 6 SDK folder (recommended)" -ForegroundColor Yellow
    Write-Host "     Typical path: $wmSdk" -ForegroundColor Yellow
    $need++
} else {
    Write-Host "[OK] Windows Mobile 6 SDK" -ForegroundColor Green
    Write-Host "     $wmSdk" -ForegroundColor DarkGray
    $ok++
}

$proj = Join-Path $PSScriptRoot "..\handheld-ce\MerlinInventoryTest\MerlinInventoryTest.csproj"
$null = Test-ItemReport "Project file" $proj

$cab = Join-Path $PSScriptRoot "..\public\deploy\MerlinInventoryTest.cab"
if (Test-Path $cab) {
    $kb = [math]::Round((Get-Item $cab).Length / 1KB, 1)
    Write-Host "[OK] CAB ready for deploy ($kb KB)" -ForegroundColor Green
    Write-Host "     $cab" -ForegroundColor DarkGray
    $ok++
} else {
    Write-Host "[--] No CAB at public/deploy/MerlinInventoryTest.cab yet" -ForegroundColor Yellow
}

Write-Host ""
$isStandard = $vsSku -match "Standard|Express"
if ($hasCfTargets -and (Test-Path $vsDevenv) -and -not $isStandard) {
    Write-Host "Verdict: READY to build in VS2008 (open MerlinInventoryTest.csproj, Release, Build)." -ForegroundColor Green
} elseif ($isStandard) {
    Write-Host "Verdict: VS2008 Standard cannot build the native Merlin app." -ForegroundColor Red
    Write-Host "  Install VS2008 Professional, then run .\scripts\install-handheld-prereqs.ps1" -ForegroundColor Yellow
    Write-Host "  Or use the browser on the gun: http://YOUR_SERVER:3000/mobile.html" -ForegroundColor Yellow
} elseif (Test-Path $vsDevenv) {
    Write-Host "Verdict: VS2008 is installed but NOT ready for CE yet." -ForegroundColor Yellow
    Write-Host "  1) Use VS2008 Professional (Smart Device support)" -ForegroundColor Yellow
    Write-Host "  2) Run .\scripts\install-handheld-prereqs.ps1 (CF + WM6 SDK)" -ForegroundColor Yellow
    Write-Host "  3) Re-run this script" -ForegroundColor Yellow
} else {
    Write-Host "Verdict: Install Visual Studio 2008 first." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Details: docs/BUILD_HANDHELD_WALKTHROUGH.md" -ForegroundColor Gray
