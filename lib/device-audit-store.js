const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const AUDIT_DIR = path.join(__dirname, '..', 'data', 'device-audits');
const MAX_REPORT_BYTES = 2 * 1024 * 1024;
const MAX_STORED_REPORTS = 500;

function ensureDir() {
    if (!fs.existsSync(AUDIT_DIR)) {
        fs.mkdirSync(AUDIT_DIR, { recursive: true });
    }
}

function sanitizeScannerId(scannerId) {
    return String(scannerId || 'unknown')
        .trim()
        .replace(/[^a-zA-Z0-9._-]+/g, '_')
        .slice(0, 64) || 'unknown';
}

function saveAuditReport(scannerId, body) {
    ensureDir();
    const id = Date.now() + '-' + crypto.randomBytes(4).toString('hex');
    const capturedAt =
        body && body.captured_at != null ? Number(body.captured_at) : Date.now();
    const record = {
        id,
        scanner_id: sanitizeScannerId(scannerId),
        captured_at: Number.isFinite(capturedAt) ? capturedAt : Date.now(),
        app_version: body && body.app_version != null ? String(body.app_version) : '',
        report: body && body.report != null ? body.report : body || {},
    };

    const json = JSON.stringify(record, null, 2);
    if (json.length > MAX_REPORT_BYTES) {
        const err = new Error('audit report exceeds size limit');
        err.status = 413;
        throw err;
    }

    fs.writeFileSync(path.join(AUDIT_DIR, id + '.json'), json, 'utf8');
    pruneOldReports();
    return record;
}

function pruneOldReports() {
    ensureDir();
    const files = fs
        .readdirSync(AUDIT_DIR)
        .filter((f) => f.endsWith('.json'))
        .map((f) => {
            const full = path.join(AUDIT_DIR, f);
            return { full, mtime: fs.statSync(full).mtimeMs };
        })
        .sort((a, b) => b.mtime - a.mtime);

    for (let i = MAX_STORED_REPORTS; i < files.length; i++) {
        try {
            fs.unlinkSync(files[i].full);
        } catch {
            /* ignore */
        }
    }
}

function readRecordFile(filePath) {
    const raw = fs.readFileSync(filePath, 'utf8');
    return JSON.parse(raw);
}

function listAuditReports(options = {}) {
    ensureDir();
    const scannerFilter =
        options.scanner_id == null ? '' : sanitizeScannerId(options.scanner_id);
    const limit = Math.min(Math.max(parseInt(options.limit, 10) || 50, 1), 200);

    const rows = fs
        .readdirSync(AUDIT_DIR)
        .filter((f) => f.endsWith('.json'))
        .map((f) => {
            const full = path.join(AUDIT_DIR, f);
            try {
                const rec = readRecordFile(full);
                return {
                    id: rec.id || f.replace(/\.json$/, ''),
                    scanner_id: rec.scanner_id || '',
                    captured_at: rec.captured_at || 0,
                    app_version: rec.app_version || '',
                    bytes: fs.statSync(full).size,
                    summary: summarizeReport(rec.report),
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

function summarizeReport(report) {
    if (!report || typeof report !== 'object') return {};
    const system = report.system || {};
    const known = Array.isArray(report.known_apps) ? report.known_apps : [];
    const files = Array.isArray(report.installed_files) ? report.installed_files : [];
    const nur = report.nur_discovery && typeof report.nur_discovery === 'object' ? report.nur_discovery : {};
    return {
        machine: system.machine_name || '',
        os: system.os_version || '',
        known_app_count: known.length,
        file_count: files.length,
        ping_ok: report.network && report.network.ping_ok === true,
        scan_captures:
            report.scan_session && Array.isArray(report.scan_session.events)
                ? report.scan_session.events.length
                : 0,
        scan_completed: !!(report.scan_session && report.scan_session.completed),
        nur_dotnet_ready: nur.dotnet_ready === true,
        nur_best_path: nur.best_path || '',
        nur_candidate_count: Array.isArray(nur.candidates) ? nur.candidates.length : 0,
    };
}

function getAuditReport(id) {
    ensureDir();
    const safe = String(id || '').replace(/[^a-zA-Z0-9._-]+/g, '');
    if (!safe) return null;
    const full = path.join(AUDIT_DIR, safe + '.json');
    if (!fs.existsSync(full)) return null;
    return readRecordFile(full);
}

function listScannerIds() {
    ensureDir();
    const ids = new Set();
    for (const f of fs.readdirSync(AUDIT_DIR)) {
        if (!f.endsWith('.json')) continue;
        try {
            const rec = readRecordFile(path.join(AUDIT_DIR, f));
            if (rec.scanner_id) ids.add(rec.scanner_id);
        } catch {
            /* ignore */
        }
    }
    return [...ids].sort();
}

module.exports = {
    AUDIT_DIR,
    saveAuditReport,
    listAuditReports,
    getAuditReport,
    listScannerIds,
    summarizeReport,
};
