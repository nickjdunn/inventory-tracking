# Copy a built MerlinInventoryTest.cab into public/deploy for Wi-Fi install on the gun.
param(
    [Parameter(Mandatory = $true)]
    [string] $CabPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

& (Join-Path $PSScriptRoot 'Invoke-VersionSync.ps1') -RepoRoot $repoRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$destDir = Join-Path $repoRoot 'public\deploy'
$destFile = Join-Path $destDir 'MerlinInventoryTest.cab'

if (-not (Test-Path $CabPath)) {
    Write-Error "CAB not found: $CabPath"
}

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
Copy-Item -Path $CabPath -Destination $destFile -Force

$sizeKb = [math]::Round((Get-Item $destFile).Length / 1KB, 1)
Write-Host "Published: $destFile ($sizeKb KB)"

$uninstallSrc = Join-Path (Split-Path $CabPath -Parent) 'MerlinInventoryUninstall.cab'
$uninstallDest = Join-Path $destDir 'MerlinInventoryUninstall.cab'
if (Test-Path $uninstallSrc) {
    Copy-Item -Path $uninstallSrc -Destination $uninstallDest -Force
    $unKb = [math]::Round((Get-Item $uninstallDest).Length / 1KB, 1)
    Write-Host "Published: $uninstallDest ($unKb KB)"
}

Write-Host "On Merlin browser open: http://<server-ip>:3000/deploy/"
