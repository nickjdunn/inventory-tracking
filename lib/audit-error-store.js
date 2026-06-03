const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const ERROR_DIR = path.join(__dirname, '..', 'data', 'audit-errors');
const MAX_DETAIL_CHARS = 32000;
const MAX_STORED = 300;

function ensureDir() {
    if (!fs.existsSync(ERROR_DIR)) {
        fs.mkdirSync(ERROR_DIR, { recursive: true });
    }
}

function sanitizeScannerId(scannerId) {
    return String(scannerId || 'unknown')
        .trim()
        .replace(/[^a-zA-Z0-9._-]+/g, '_')
        .slice(0, 64) || 'unknown';
}

function saveAuditError(body) {
    ensureDir();
    const scannerId = sanitizeScannerId(body.scanner_id);
    const id = Date.now() + '-' + crypto.randomBytes(4).toString('hex');
    const capturedAt =
        body.captured_at != null ? Number(body.captured_at) : Date.now();
    const detail = String(body.detail || body.message || '').slice(0, MAX_DETAIL_CHARS);

    const record = {
        id,
        scanner_id: scannerId,
        captured_at: Number.isFinite(capturedAt) ? capturedAt : Date.now(),
        app_version: body.app_version != null ? String(body.app_version) : '',
        context: body.context != null ? String(body.context).slice(0, 120) : '',
        message: body.message != null ? String(body.message).slice(0, 2000) : '',
        detail,
        nur_status: body.nur_status != null ? String(body.nur_status).slice(0, 500) : '',
    };

    fs.writeFileSync(path.join(ERROR_DIR, id + '.json'), JSON.stringify(record, null, 2), 'utf8');
    pruneOld();
    return record;
}

function pruneOld() {
    ensureDir();
    const files = fs
        .readdirSync(ERROR_DIR)
        .filter((f) => f.endsWith('.json'))
        .map((f) => {
            const full = path.join(ERROR_DIR, f);
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
    const full = path.join(ERROR_DIR, safe + '.json');
    if (!fs.existsSync(full)) return null;
    return JSON.parse(fs.readFileSync(full, 'utf8'));
}

function listAuditErrors(options = {}) {
    ensureDir();
    const scannerFilter =
        options.scanner_id == null ? '' : sanitizeScannerId(options.scanner_id);
    const limit = Math.min(Math.max(parseInt(options.limit, 10) || 50, 1), 200);

    const rows = fs
        .readdirSync(ERROR_DIR)
        .filter((f) => f.endsWith('.json'))
        .map((f) => {
            try {
                const rec = JSON.parse(fs.readFileSync(path.join(ERROR_DIR, f), 'utf8'));
                return {
                    id: rec.id || f.replace(/\.json$/, ''),
                    scanner_id: rec.scanner_id || '',
                    captured_at: rec.captured_at || 0,
                    app_version: rec.app_version || '',
                    context: rec.context || '',
                    message: (rec.message || '').slice(0, 200),
                    detail_bytes: (rec.detail || '').length,
                };
            } catch {
                return null;
            }
        })
        .filter(Boolean)
        .filter((row) => !scannerFilter || row.scanner_id === scannerFilter)
        .sort((a, b) => b.captured_at - a.captured_at)
        .slice(0, limit);

    return rows;
}

function listScannerIds() {
    ensureDir();
    const ids = new Set();
    for (const f of fs.readdirSync(ERROR_DIR)) {
        if (!f.endsWith('.json')) continue;
        try {
            const rec = JSON.parse(fs.readFileSync(path.join(ERROR_DIR, f), 'utf8'));
            if (rec.scanner_id) ids.add(rec.scanner_id);
        } catch {
            /* ignore */
        }
    }
    return [...ids].sort();
}

module.exports = {
    ERROR_DIR,
    saveAuditError,
    listAuditErrors,
    getAuditError: readRecord,
    listScannerIds,
};
