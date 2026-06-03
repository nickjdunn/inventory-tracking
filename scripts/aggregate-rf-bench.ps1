# Aggregate merlin-rf-bench-v1 traces (single file, bundle, or folder of JSON).
param(
    [string]$JsonPath = "",
    [string]$FolderPath = "",
    [string]$OutCsv = ""
)

function Get-StackKey($session) {
    $st = $session.stack_setup
    if (-not $st) { return "no_stack" }
    $d = if ($st.distance_inches_text) { $st.distance_inches_text } else { $st.distance_inches }
    $c = if ($st.tag_count_text) { $st.tag_count_text } else { $st.tag_count }
    $s = if ($st.spacing_inches_text) { $st.spacing_inches_text } else { $st.spacing_inches }
    return "d${d}_n${c}_s${s}"
}

function Import-TraceRecords($path) {
    $raw = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $data = $raw | ConvertFrom-Json
    if ($data.format -eq 'merlin-rssi-bench-bundle-v1' -and $data.traces) {
        return @($data.traces)
    }
    if ($data.session) { return @($data) }
    if ($data.trace) { return @($data.trace) }
    return @($data)
}

$records = @()
if ($JsonPath) {
    $records += Import-TraceRecords $JsonPath
}
if ($FolderPath) {
    Get-ChildItem -LiteralPath $FolderPath -Filter *.json | ForEach-Object {
        try { $records += Import-TraceRecords $_.FullName } catch { Write-Warning $_.Name }
    }
}
if (-not $records.Count) {
    Write-Host "Usage: -JsonPath file.json OR -FolderPath dir (bundle or trace files)"
    exit 1
}

$rows = @()
foreach ($rec in $records) {
    $session = if ($rec.session) { $rec.session } else { $rec }
    if ($session.format -ne 'merlin-rf-bench-v1') { continue }
    $stackKey = Get-StackKey $session
    $best = $session.bench_results | Sort-Object { -$_.score } | Select-Object -First 1
    if (-not $best) { continue }
    $rows += [PSCustomObject]@{
        trace_id = $rec.id
        captured_at = $rec.captured_at
        stack_key = $stackKey
        stack_summary = $session.stack_setup.summary
        target_epc = $session.target_epc
        best_preset = $best.preset_id
        best_label = $best.label
        hit_pct = $best.hit_pct
        score = $best.score
        avg_rssi = $best.avg_rssi
        other_tags = $best.other_tags_total
    }
}

Write-Host "Bench traces analyzed: $($rows.Count)"
$rows | Group-Object stack_key | ForEach-Object {
    Write-Host ""
    Write-Host "=== Stack: $($_.Name) ($($_.Count) runs) ==="
    $_.Group | Sort-Object { -$_.score } | Select-Object -First 5 | ForEach-Object {
        Write-Host ("  {0} hit={1}% score={2} avg={3} noise={4}" -f `
            $_.best_preset, $_.hit_pct, $_.score, $_.avg_rssi, $_.other_tags)
    }
    $top = $_.Group | Sort-Object { -$_.score } | Select-Object -First 1
    if ($top) {
        Write-Host ("  >> Recommend for this stack: {0} ({1})" -f $top.best_preset, $top.best_label)
    }
}

if ($OutCsv) {
    $rows | Export-Csv -LiteralPath $OutCsv -NoTypeInformation -Encoding UTF8
    Write-Host ""
    Write-Host "Wrote CSV: $OutCsv"
}
