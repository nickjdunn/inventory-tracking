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

    const record = {
        id,
        scanner_id: scannerId,
        captured_at: Number.isFinite(capturedAt) ? capturedAt : Date.now(),
        app_version: body.app_version != null ? String(body.app_version) : '',
        target_epc: session.target_epc != null ? String(session.target_epc) : '',
        sample_count: samples.length,
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

function readRecord(id) {
    ensureDir();
    const safe = String(id || '').replace(/[^a-zA-Z0-9._-]+/g, '');
    if (!safe) return null;
    const full = path.join(RSSI_DIR, safe + '.json');
    if (!fs.existsSync(full)) return null;
    return JSON.parse(fs.readFileSync(full, 'utf8'));
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
                return {
                    id: rec.id || f.replace(/\.json$/, ''),
                    scanner_id: rec.scanner_id || '',
                    captured_at: rec.captured_at || 0,
                    app_version: rec.app_version || '',
                    target_epc: (rec.target_epc || '').slice(0, 24),
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
};
