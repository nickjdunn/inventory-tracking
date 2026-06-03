# Summarize merlin-rf-bench-v1 JSON (uploaded trace or local pending file).
param(
    [Parameter(Mandatory = $true)]
    [string]$JsonPath
)

$raw = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8
$data = $raw | ConvertFrom-Json
$session = $data.session
if (-not $session) { $session = $data }
if ($session.format -ne 'merlin-rf-bench-v1') {
    Write-Host "Not merlin-rf-bench-v1 (format=$($session.format))"
    exit 1
}

Write-Host "RF bench — target $($session.target_epc)"
Write-Host "Pulses per preset: $($session.pulses_per_preset)"
if ($session.stack_setup) {
    $st = $session.stack_setup
    $sum = if ($st.summary) { $st.summary } else {
        "$($st.distance_inches) in / $($st.tag_count) tags / $($st.spacing_inches) in apart"
    }
    Write-Host "Tag stack: $sum"
}
Write-Host ""
$session.bench_results | Sort-Object { -$_.score } | ForEach-Object -Begin { $i = 0 } -Process {
    $i++
    Write-Host ("{0}. {1} — hit {2}% score {3} avg_RSSI {4} noise_tags {5}" -f `
        $i, $_.label, $_.hit_pct, $_.score, $_.avg_rssi, $_.other_tags_total)
}
if ($session.diag_tests) {
    Write-Host ""
    Write-Host "Diag on best preset:"
    $session.diag_tests | ForEach-Object {
        Write-Host ("  {0}: {1}/{2} hits avg_RSSI {3}" -f $_.test_id, $_.hits, $_.pulses, $_.avg_rssi)
    }
}
