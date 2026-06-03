# Upload Nordic NUR .NET DLL to the inventory server (CE guns use NurApiDotNetWCE.dll).
param(
    [Parameter(Mandatory = $true)]
    [string]$DllPath,
    [string]$ServerUrl = "http://10.17.17.17:3000",
    [ValidateSet('auto', 'wce', 'desktop')]
    [string]$Target = 'auto'
)

$ErrorActionPreference = 'Stop'
$resolved = Resolve-Path $DllPath
$name = [System.IO.Path]::GetFileName($resolved)
if ($Target -eq 'auto') {
    if ($name -match 'WCE') { $Target = 'wce' }
    else { $Target = 'desktop' }
}
$uploadName = if ($Target -eq 'wce') { 'NurApiDotNetWCE.dll' } else { 'NurApiDotNet.dll' }

$bytes = [System.IO.File]::ReadAllBytes($resolved)
if ($bytes.Length -lt 512) {
    Write-Error "File too small — is this the correct NurApi .NET DLL?"
}

$base = $ServerUrl.TrimEnd('/')
$uploadUrl = "$base/api/deploy/nur-dll?filename=$uploadName"

Write-Host "Uploading $name ($($bytes.Length) bytes) as $uploadName ..." -ForegroundColor Cyan

$resp = Invoke-WebRequest -Uri $uploadUrl -Method POST -Body $bytes `
    -ContentType 'application/octet-stream' -UseBasicParsing

Write-Host $resp.Content
Write-Host ""
Write-Host "Gun download: $base/deploy/nur/$uploadName" -ForegroundColor Green
