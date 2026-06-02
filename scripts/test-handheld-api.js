#!/usr/bin/env node
/**
 * Smoke-test handheld / deploy APIs (no CE toolchain required).
 * Usage: node scripts/test-handheld-api.js [baseUrl]
 */
const base = (process.argv[2] || 'http://127.0.0.1:3000').replace(/\/$/, '');

async function get(path) {
    const res = await fetch(`${base}${path}`);
    const text = await res.text();
    let data;
    try {
        data = JSON.parse(text);
    } catch {
        data = { _raw: text.slice(0, 200) };
    }
    return { ok: res.ok, status: res.status, data };
}

async function post(path, body) {
    const res = await fetch(`${base}${path}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
    });
    const data = await res.json().catch(() => ({}));
    return { ok: res.ok, status: res.status, data };
}

function assert(label, cond, detail) {
    const mark = cond ? 'PASS' : 'FAIL';
    console.log(`${mark} ${label}${detail ? ` — ${detail}` : ''}`);
    return cond;
}

async function main() {
    console.log(`Handheld API smoke test → ${base}\n`);
    let passed = 0;
    let failed = 0;

    const ping = await get('/api/ping');
    if (assert('GET /api/ping', ping.ok && ping.data.ok)) passed++;
    else failed++;

    const summary = await get('/api/handheld/sync-summary');
    const hasQueue = Array.isArray(summary.data.activeSearchQueue);
    if (
        assert(
            'GET /api/handheld/sync-summary',
            summary.ok && summary.data.ok && hasQueue,
            hasQueue ? `items=${summary.data.item_count}` : 'missing activeSearchQueue'
        )
    ) {
        passed++;
    } else failed++;

    const sync = await get('/api/handheld/sync');
    if (
        assert(
            'GET /api/handheld/sync',
            sync.ok && Array.isArray(sync.data.items) && Array.isArray(sync.data.containers),
            `items=${(sync.data.items || []).length}`
        )
    ) {
        passed++;
    } else failed++;

    const hunt = await get('/api/search/target');
    if (
        assert(
            'GET /api/search/target',
            hunt.ok && Array.isArray(hunt.data.activeSearchQueue),
            `revision=${hunt.data.revision}`
        )
    ) {
        passed++;
    } else failed++;

    const nf = await post('/api/scan/near-field-ingest', {
        scanned_tags: [{ epc: 'SMOKE-TEST-EPC', rssi: -50 }],
    });
    if (assert('POST /api/scan/near-field-ingest', nf.ok && nf.data.status === 'success')) passed++;
    else failed++;

    const deploy = await get('/api/deploy/info');
    if (assert('GET /api/deploy/info', deploy.ok && deploy.data.app)) passed++;
    else failed++;

    console.log(`\n${passed} passed, ${failed} failed`);
    process.exit(failed > 0 ? 1 : 0);
}

main().catch((err) => {
    console.error(err.message || err);
    process.exit(1);
});
