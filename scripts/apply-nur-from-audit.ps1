# Reads a device-audit JSON export and prints the NUR DLL path from your gun.
param(
    [Parameter(Mandatory = $true)]
    [string]$AuditJsonPath
)

$ErrorActionPreference = 'Stop'
$json = Get-Content -Raw -Path $AuditJsonPath | ConvertFrom-Json
$rep = $json.report
if (-not $rep) { $rep = $json }

$nur = $rep.nur_discovery
if (-not $nur) {
    Write-Error 'No nur_discovery in this report.'
}

Write-Host "dotnet_ready: $($nur.dotnet_ready)" -ForegroundColor Cyan
Write-Host "best_path:    $($nur.best_path)"
Write-Host "copied:       $($nur.installed_beside_app)"
Write-Host ""
Write-Host "Your Merlin already has the CE .NET DLL at:" -ForegroundColor Green
Write-Host "  \Windows\NurApiDotNetWCE.dll"
Write-Host ""
Write-Host "No PC SDK upload needed. Install audit-1.1.4+ CAB or add to merlin-audit.cfg on gun:"
Write-Host "  nur_dll=\Windows\NurApiDotNetWCE.dll"

if ($nur.candidates) {
    Write-Host ""
    Write-Host "Candidates:"
    $nur.candidates | ForEach-Object {
        $flag = if ($_.dotnet_loadable) { '[.NET OK]' } else { '' }
        Write-Host "  $($_.path) $flag $($_.note)"
    }
}
