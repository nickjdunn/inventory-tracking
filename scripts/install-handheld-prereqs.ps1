# Downloads and installs .NET CF 3.5 + Windows Mobile 6 Professional SDK for Merlin CE builds.
# Run in an elevated PowerShell: Right-click -> Run as administrator
#   cd path\to\inventory-tracking
#   .\scripts\install-handheld-prereqs.ps1

$ErrorActionPreference = "Stop"
$dlDir = Join-Path $env:USERPROFILE "Downloads\merlin-ce-build"
New-Item -ItemType Directory -Force -Path $dlDir | Out-Null

$netCfUrl = "https://download.microsoft.com/download/4/7/7/477deb53-d1bb-4200-a426-c5085760704a/NetCFSetupv35.msi"
$netCfMsi = Join-Path $dlDir "NetCFSetupv35.msi"

# Microsoft-hosted WM6 link often 404; Internet Archive hosts the same official MSI.
$wm6Url = "https://archive.org/download/windows-mobile-6-sdk/Windows%20Mobile%206%20Professional%20SDK%20Refresh.msi"
$wm6Msi = Join-Path $dlDir "Windows Mobile 6 Professional SDK Refresh.msi"

function Get-FileIfMissing {
    param([string]$Url, [string]$OutPath, [string]$Label)
    if ((Test-Path $OutPath) -and ((Get-Item $OutPath).Length -gt 1MB)) {
        Write-Host "[skip] $Label already downloaded" -ForegroundColor DarkGray
        return
    }
    Write-Host "[download] $Label ..." -ForegroundColor Cyan
    Write-Host "  $Url"
    $progressPreference = "Continue"
    Invoke-WebRequest -Uri $Url -OutFile $OutPath -UseBasicParsing
    $mb = [math]::Round((Get-Item $OutPath).Length / 1MB, 1)
    Write-Host "[done] $mb MB -> $OutPath" -ForegroundColor Green
}

function Install-Msi {
    param([string]$Path, [string]$Label)
    Write-Host "[install] $Label" -ForegroundColor Cyan
    $p = Start-Process -FilePath "msiexec.exe" -ArgumentList @("/i", "`"$Path`"", "/qn", "/norestart") -Wait -PassThru
    if ($p.ExitCode -eq 0 -or $p.ExitCode -eq 3010) {
        Write-Host "[ok] $Label (exit $($p.ExitCode))" -ForegroundColor Green
    } else {
        Write-Host "[warn] $Label exit code $($p.ExitCode) - try running the MSI manually" -ForegroundColor Yellow
    }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Not running as Administrator. MSI installs may fail." -ForegroundColor Yellow
    Write-Host "Re-run: Right-click PowerShell -> Run as administrator, then run this script again." -ForegroundColor Yellow
}

Write-Host "=== Merlin CE build prerequisites ===" -ForegroundColor Cyan
Write-Host "Download folder: $dlDir`n"

Get-FileIfMissing -Url $netCfUrl -OutPath $netCfMsi -Label ".NET Compact Framework 3.5"
Install-Msi -Path $netCfMsi -Label ".NET Compact Framework 3.5"

Get-FileIfMissing -Url $wm6Url -OutPath $wm6Msi -Label "Windows Mobile 6 Professional SDK (~455 MB)"
Install-Msi -Path $wm6Msi -Label "Windows Mobile 6 Professional SDK"

$vsFolder = Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio 9.0" -Directory -Filter "Microsoft Visual Studio 2008*" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($vsFolder -and $vsFolder.Name -match "Standard|Express") {
    Write-Host ""
    Write-Host "=== VS2008 Standard/Express cannot build CE apps ===" -ForegroundColor Red
    Write-Host @"
Smart Device Programmability is only in Professional, Team, or Enterprise.
Options:
  A) Install VS2008 Professional (90-day trial ISO from Microsoft archives)
  B) Keep using the browser on the Merlin: http://YOUR_SERVER:3000/mobile.html

After Professional is installed, re-run this script to install the WM6 SDK MSI.
"@
} else {
    Write-Host ""
    Write-Host "=== Enable Smart Device in Visual Studio 2008 ===" -ForegroundColor Cyan
    Write-Host @"
If the check script still reports missing Smart Device support:
  Run VS2008 Setup -> Modify -> enable Smart Device / Visual C++ Smart Device Programmability
  Then re-run: .\scripts\install-handheld-prereqs.ps1
"@
}

Write-Host ""
& (Join-Path $PSScriptRoot "check-handheld-build-env.ps1")
