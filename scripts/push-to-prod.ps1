# Push code to Proxmox server (bare repo) and publish CAB files.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

Set-Location $repoRoot

Write-Host "Pushing main -> prod (repo.git on server)..." -ForegroundColor Cyan
git push prod main
if ($LASTEXITCODE -ne 0) {
    Write-Error "git push prod main failed. Check SSH access to root@10.17.17.17"
}

Write-Host "Git push OK. If post-receive hook is installed, app/ + pm2 updated automatically." -ForegroundColor Green

$deployDir = Join-Path $repoRoot 'public\deploy'
$cabs = @(
    'MerlinInventoryTest.cab',
    'MerlinDeviceAudit.cab',
    'MerlinStreamTest.cab'
)

Write-Host ""
Write-Host "Publishing CAB files via scp..." -ForegroundColor Cyan
$remote = 'root@10.17.17.17:/opt/inventory-app/app/public/deploy/'
foreach ($name in $cabs) {
    $local = Join-Path $deployDir $name
    if (-not (Test-Path $local)) {
        Write-Host "  skip $name (not built locally)" -ForegroundColor Yellow
        continue
    }
    Write-Host "  scp $name"
    scp $local $remote
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "scp failed for $name"
    }
}

Write-Host ""
Write-Host "Done. Gun deploy hub: http://10.17.17.17:3000/deploy/" -ForegroundColor Green
