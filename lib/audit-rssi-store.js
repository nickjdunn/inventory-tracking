const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const RSSI_DIR = path.join(__dirname, '..', 'data', 'audit-rssi');
const MAX_STORED = 200;

function ensureDir() {
    if (!fs.existsSync(RSSI_DIR)) {
        fs.mkdirSync(RSSI_DIR, { recursive: true });
    }
}

function sanitizeScannerId(scannerId) {
    return String(scannerId || 'unknown')
        .trim()
        .replace(/[^a-zA-Z0-9._-]+/g, '_')
        .slice(0, 64) || 'unknown';
}

function saveAuditRssiTrace(body) {
    ensureDir();
    const scannerId = sanitizeScannerId(body.scanner_id);
    const id = Date.now() + '-' + crypto.randomBytes(4).toString('hex');
    const capturedAt =
        body.captured_at != null ? Number(body.captured_at) : Date.now();
    const session = body.session != null ? body.session : {};
    const samples = Array.isArray(session.samples) ? session.samples : [];
    const benchResults = Array.isArray(session.bench_results) ? session.bench_results : [];
    const format = session.format != null ? String(session.format) : '';

    const stack = session.stack_setup != null ? session.stack_setup : null;

    const record = {
        id,
        scanner_id: scannerId,
        captured_at: Number.isFinite(capturedAt) ? capturedAt : Date.now(),
        app_version: body.app_version != null ? String(body.app_version) : '',
        target_epc: session.target_epc != null ? String(session.target_epc) : '',
        trace_format: format,
        stack_label: stackLabelFromStack(stack),
        sample_count: benchResults.length > 0 ? benchResults.length : samples.length,
        session,
    };

    fs.writeFileSync(path.join(RSSI_DIR, id + '.json'), JSON.stringify(record, null, 2), 'utf8');
    pruneOld();
    return record;
}

function pruneOld() {
    ensureDir();
    const files = fs
        .readdirSync(RSSI_DIR)
        .filter((f) => f.endsWith('.json'))
        .map((f) => {
            const full = path.join(RSSI_DIR, f);
            return { full, mtime: fs.statSync(full).mtimeMs };
        })
        .sort((a, b) => b.mtime - a.mtime);

    for (let i = MAX_STORED; i < files.length; i++) {
        try {
            fs.unlinkSync(files[i].full);
        } catch {
            /* ignore */
        }
    }
}

function stackLabelFromStack(stack) {
    if (!stack || typeof stack !== 'object') return '';
    if (stack.summary) return String(stack.summary);
    const d = stack.distance_inches_text != null ? stack.distance_inches_text : stack.distance_inches;
    const c = stack.tag_count_text != null ? stack.tag_count_text : stack.tag_count;
    const s = stack.spacing_inches_text != null ? stack.spacing_inches_text : stack.spacing_inches;
    if (d == null && c == null) return '';
    return `${d != null ? d : '?'} in · ${c != null ? c : '?'} tags · ${s != null ? s : '?'} in apart`;
}

function sanitizeTraceId(id) {
    return String(id || '').replace(/[^a-zA-Z0-9._-]+/g, '');
}

function readRecord(id) {
    ensureDir();
    const safe = sanitizeTraceId(id);
    if (!safe) return null;
    const full = path.join(RSSI_DIR, safe + '.json');
    if (!fs.existsSync(full)) return null;
    return JSON.parse(fs.readFileSync(full, 'utf8'));
}

function deleteAuditRssiTrace(id) {
    ensureDir();
    const safe = sanitizeTraceId(id);
    if (!safe) return false;
    const full = path.join(RSSI_DIR, safe + '.json');
    if (!fs.existsSync(full)) return false;
    fs.unlinkSync(full);
    return true;
}

function deleteAuditRssiTraces(ids) {
    const list = Array.isArray(ids) ? ids : [];
    let deleted = 0;
    const errors = [];
    for (const raw of list) {
        try {
            if (deleteAuditRssiTrace(raw)) deleted += 1;
            else errors.push({ id: raw, error: 'not found' });
        } catch (err) {
            errors.push({ id: raw, error: err.message || 'delete failed' });
        }
    }
    return { deleted, errors };
}

function getAuditRssiTracesBulk(ids) {
    const list = Array.isArray(ids) ? ids.slice(0, 80) : [];
    const traces = [];
    const missing = [];
    for (const raw of list) {
        const rec = readRecord(raw);
        if (rec) traces.push(rec);
        else missing.push(raw);
    }
    return { traces, missing };
}

function listAuditRssiTraces(options = {}) {
    ensureDir();
    const scannerFilter =
        options.scanner_id == null ? '' : sanitizeScannerId(options.scanner_id);
    const limit = Math.min(Math.max(parseInt(options.limit, 10) || 50, 1), 200);

    return fs
        .readdirSync(RSSI_DIR)
        .filter((f) => f.endsWith('.json'))
        .map((f) => {
            try {
                const rec = JSON.parse(fs.readFileSync(path.join(RSSI_DIR, f), 'utf8'));
                const session = rec.session || {};
                const stack =
                    rec.stack_label ||
                    stackLabelFromStack(session.stack_setup) ||
                    '';
                return {
                    id: rec.id || f.replace(/\.json$/, ''),
                    scanner_id: rec.scanner_id || '',
                    captured_at: rec.captured_at || 0,
                    app_version: rec.app_version || '',
                    target_epc: (rec.target_epc || '').slice(0, 24),
                    trace_format: rec.trace_format || session.format || '',
                    stack_label: stack,
                    sample_count: rec.sample_count || 0,
                };
            } catch {
                return null;
            }
        })
        .filter(Boolean)
        .filter((row) => !scannerFilter || row.scanner_id === scannerFilter)
        .sort((a, b) => b.captured_at - a.captured_at)
        .slice(0, limit);
}

module.exports = {
    RSSI_DIR,
    saveAuditRssiTrace,
    listAuditRssiTraces,
    getAuditRssiTrace: readRecord,
    deleteAuditRssiTrace,
    deleteAuditRssiTraces,
    getAuditRssiTracesBulk,
};
