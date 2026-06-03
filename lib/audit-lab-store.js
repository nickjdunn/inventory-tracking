const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const LAB_DIR = path.join(__dirname, '..', 'data', 'audit-lab');
const MAX_STORED = 200;

function ensureDir() {
    if (!fs.existsSync(LAB_DIR)) {
        fs.mkdirSync(LAB_DIR, { recursive: true });
    }
}

function sanitizeScannerId(scannerId) {
    return String(scannerId || 'unknown')
        .trim()
        .replace(/[^a-zA-Z0-9._-]+/g, '_')
        .slice(0, 64) || 'unknown';
}

function saveAuditLab(body) {
    ensureDir();
    const scannerId = sanitizeScannerId(body.scanner_id);
    const id = Date.now() + '-' + crypto.randomBytes(4).toString('hex');
    const capturedAt =
        body.captured_at != null ? Number(body.captured_at) : Date.now();
    const session = body.session != null ? body.session : {};

    const record = {
        id,
        scanner_id: scannerId,
        captured_at: Number.isFinite(capturedAt) ? capturedAt : Date.now(),
        app_version: body.app_version != null ? String(body.app_version) : '',
        session,
        event_count: Array.isArray(session.events) ? session.events.length : 0,
    };

    fs.writeFileSync(path.join(LAB_DIR, id + '.json'), JSON.stringify(record, null, 2), 'utf8');
    pruneOld();
    return record;
}

function pruneOld() {
    ensureDir();
    const files = fs
        .readdirSync(LAB_DIR)
        .filter((f) => f.endsWith('.json'))
        .map((f) => {
            const full = path.join(LAB_DIR, f);
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
    const full = path.join(LAB_DIR, safe + '.json');
    if (!fs.existsSync(full)) return null;
    return JSON.parse(fs.readFileSync(full, 'utf8'));
}

function listAuditLab(options = {}) {
    ensureDir();
    const scannerFilter =
        options.scanner_id == null ? '' : sanitizeScannerId(options.scanner_id);
    const limit = Math.min(Math.max(parseInt(options.limit, 10) || 50, 1), 200);

    return fs
        .readdirSync(LAB_DIR)
        .filter((f) => f.endsWith('.json'))
        .map((f) => {
            try {
                const rec = JSON.parse(fs.readFileSync(path.join(LAB_DIR, f), 'utf8'));
                return {
                    id: rec.id || f.replace(/\.json$/, ''),
                    scanner_id: rec.scanner_id || '',
                    captured_at: rec.captured_at || 0,
                    app_version: rec.app_version || '',
                    event_count: rec.event_count || 0,
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
    LAB_DIR,
    saveAuditLab,
    listAuditLab,
    getAuditLab: readRecord,
};
