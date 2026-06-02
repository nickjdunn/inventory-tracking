# Sync version.generated.json + native AppConfig/AssemblyInfo (node or PowerShell fallback).
param(
    [string] $RepoRoot = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
)

$ErrorActionPreference = 'Stop'

function Resolve-NodeExe {
    $cmd = Get-Command node -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { return $cmd.Source }
    $candidates = @(
        "${env:ProgramFiles}\nodejs\node.exe",
        "${env:ProgramFiles(x86)}\nodejs\node.exe",
        "$env:LOCALAPPDATA\Programs\node\node.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

function Invoke-Git {
    param(
        [string[]] $GitArgs,
        [string] $Fallback = ''
    )
    try {
        $out = & git -C $RepoRoot @GitArgs 2>$null
        if ($LASTEXITCODE -ne 0) { return $Fallback }
        return ($out | Select-Object -First 1).ToString().Trim()
    } catch {
        return $Fallback
    }
}

function Sync-VersionWithPowerShell {
    $versionJson = Join-Path $RepoRoot 'version.json'
    if (-not (Test-Path $versionJson)) {
        Write-Warning 'version.json missing; skipping version sync.'
        return
    }
    $base = Get-Content $versionJson -Raw | ConvertFrom-Json
    $commitCount = [int](Invoke-Git -GitArgs @('rev-list', '--count', 'HEAD') -Fallback '0')
    if ($commitCount -lt 0) { $commitCount = 0 }
    $gitHash = Invoke-Git -GitArgs @('rev-parse', '--short', 'HEAD') -Fallback 'dev'
    $version = "$($base.major).$($base.minor).$commitCount+$gitHash"
    $assemblyVersion = "$($base.major).$($base.minor).$commitCount.0"

    $generated = @{
        name            = $base.name
        version         = $version
        assemblyVersion = $assemblyVersion
        major           = $base.major
        minor           = $base.minor
        patch           = $commitCount
        gitCommit       = $gitHash
        gitCommitCount  = $commitCount
        builtAt         = (Get-Date).ToUniversalTime().ToString('o')
    }
    $json = ($generated | ConvertTo-Json -Depth 4) + [Environment]::NewLine
    Set-Content -Path (Join-Path $RepoRoot 'version.generated.json') -Value $json -Encoding UTF8
    Set-Content -Path (Join-Path $RepoRoot 'public\version.generated.json') -Value $json -Encoding UTF8

    $appConfig = Join-Path $RepoRoot 'handheld-ce\MerlinInventoryTest\AppConfig.cs'
    if (Test-Path $appConfig) {
        $lines = Get-Content $appConfig
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'public const string AppVersion') {
                $lines[$i] = '        public const string AppVersion = "' + $version + '";'
            }
        }
        Set-Content -Path $appConfig -Value $lines -Encoding UTF8
    }

    $assemblyInfo = Join-Path $RepoRoot 'handheld-ce\MerlinInventoryTest\Properties\AssemblyInfo.cs'
    if (Test-Path $assemblyInfo) {
        $lines = Get-Content $assemblyInfo
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'AssemblyVersion') {
                $lines[$i] = '[assembly: AssemblyVersion("' + $assemblyVersion + '")]'
            }
        }
        Set-Content -Path $assemblyInfo -Value $lines -Encoding UTF8
    }

    Write-Host "Version synced (PowerShell): $version (assembly $assemblyVersion)" -ForegroundColor Green
}

$node = Resolve-NodeExe
if ($node) {
    Write-Host 'Syncing version (node)...' -ForegroundColor Cyan
    & $node (Join-Path $RepoRoot 'scripts\sync-version.js')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} else {
    Write-Host 'Node.js not in PATH; using built-in version sync.' -ForegroundColor Yellow
    Sync-VersionWithPowerShell
}
