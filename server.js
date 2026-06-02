const http = require('http');
const express = require('express');
const WebSocket = require('ws');
const fs = require('fs');
const path = require('path');
const db = require('./database'); // Import our database setup
const {
    lookupUpcHybrid,
    lookupProduct,
    normalizeUpc,
    sanitizeCategoryString,
} = require('./upc-lookup');
const {
    collectUniqueCategories,
    transformCategoryField,
    splitCategoryTags,
    renameTagInList,
    deleteTagFromList,
    mergeTagInList,
} = require('./category-taxonomy');
const {
    normalizeEpc,
    normalizeBoundaryTag,
    normalizeExcludeId,
    epcEquals,
    checkEpcForRole,
    checkEpcForRoleAsync,
    validateContainerSaveAsync,
    toNearFieldReason,
} = require('./epc-registry');
const { dbAll, dbGet, dbRun } = require('./db-async');
const app = express();
const PORT = process.env.PORT || 3000;

let activeSearchQueue = [];

/** Production server never auto-fires mock scans. Simulation lives in public/emulator.html only. */
const ENABLE_SERVER_MOCK_LOOPS = process.env.ENABLE_SERVER_MOCK_LOOPS === 'true';
if (ENABLE_SERVER_MOCK_LOOPS) {
    console.warn(
        '[config] ENABLE_SERVER_MOCK_LOOPS is set but no server-side mock loop is implemented — use the emulator UI.'
    );
}

const TEST_EPC_PREFIX = 'TEST-EPC-';

/** Recent ultra-near reads for onboarding wizard (newest first). */
const nearFieldBuffer = [];
const NEAR_FIELD_BUFFER_MAX = 120;

/** Live diagnostics feed + rogue-tag discovery (hardware ingress only — no mock loops). */
const DIAGNOSTICS_BUFFER_MAX = 200;
const diagnosticsLiveBuffer = [];
const recentHardwareReads = new Map();
const RECENT_HARDWARE_READ_TTL_MS = 120_000;
const spatialHighlightUntil = new Map();
const SPATIAL_HIGHLIGHT_MS = 4500;

let knownEpcRegistryCache = null;
let knownEpcRegistryCacheAt = 0;
const KNOWN_EPC_CACHE_MS = 4000;

/** Latest RSSI per EPC while that tag is in the active hunt queue (Merlin wedge reads). */
const huntRssiByEpc = new Map();
const HUNT_SIGNAL_STALE_MS = 8000;

/** Monotonic revision — bumped when hunt RSSI or queue changes; clients use for push / long-poll. */
let huntRevision = 0;
const huntWsClients = new Set();
const huntSseClients = new Set();
const huntLongPollWaiters = [];

/** Handheld scanner heartbeats (scanner_id → { lastSeen, ...meta }). */
const scannerHeartbeats = new Map();

/** Live RFID/raw feed + uploaded diagnostic logs (per scanner_id). */
const scannerLiveFeeds = new Map();
const SCANNER_LIVE_MAX_LINES = 800;
const SCANNER_DIAG_LOG_MAX = 512000;
const SCANNER_STREAM_MAX_EVENTS = 4000;

function getScannerLiveFeed(scannerId) {
    const id = scannerId == null ? '' : String(scannerId).trim();
    if (!id) return null;
    if (!scannerLiveFeeds.has(id)) {
        scannerLiveFeeds.set(id, {
            nextId: 1,
            lines: [],
            diagLog: '',
            diagUpdated: 0,
            streamEvents: [],
            streamNextId: 1,
            streamUpdated: 0,
        });
    }
    return scannerLiveFeeds.get(id);
}

function appendScannerLiveLine(scannerId, entry) {
    const feed = getScannerLiveFeed(scannerId);
    if (!feed) return null;
    entry.id = feed.nextId++;
    entry.t = entry.t || Date.now();
    feed.lines.push(entry);
    while (feed.lines.length > SCANNER_LIVE_MAX_LINES) {
        feed.lines.shift();
    }
    return entry;
}

function tagsPreview(tagEntries, max = 12) {
    return tagEntries.slice(0, max).map((t) => ({
        epc: t.epc,
        rssi: t.rssi,
    }));
}
const SCANNER_ONLINE_THRESHOLD_MS = 45_000;

function normalizeContainerId(value) {
    const trimmed = value == null ? '' : String(value).trim();
    return trimmed === '' ? null : trimmed;
}

function computeItemStatus(item) {
    const currentId = normalizeContainerId(item.container_id);
    const homeId = normalizeContainerId(item.home_container_id);

    if (currentId && homeId && currentId === homeId) return 'HOME';
    if (!currentId && homeId) return 'FLOATING';
    if (currentId && !homeId) return 'UNASSIGNED';
    if (currentId && homeId && currentId !== homeId) return 'MISPLACED';
    return 'UNASSIGNED';
}

const ALLOWED_SETTING_KEYS = new Set([
    'home_assistant_url',
    'enable_ha_notifications',
    'rssi_near_gate',
    'rssi_far_gate',
    'upcitemdb_api_key',
]);

const SETTINGS_CACHE_MS = 5000;
let settingsCache = { value: null, at: 0 };

function getSystemSettings() {
    return new Promise((resolve, reject) => {
        db.all(`SELECT key, value FROM system_settings`, [], (err, rows) => {
            if (err) return reject(err);
            const settings = { ...DEFAULT_SYSTEM_SETTINGS_FALLBACK };
            rows.forEach((row) => {
                settings[row.key] = row.value;
            });
            resolve(settings);
        });
    });
}

function invalidateSettingsCache() {
    settingsCache = { value: null, at: 0 };
}

async function getSystemSettingsCached() {
    const now = Date.now();
    if (settingsCache.value && now - settingsCache.at < SETTINGS_CACHE_MS) {
        return settingsCache.value;
    }
    const value = await getSystemSettings();
    settingsCache = { value, at: now };
    return value;
}

const DEFAULT_SYSTEM_SETTINGS_FALLBACK = {
    home_assistant_url: '',
    enable_ha_notifications: 'false',
    rssi_near_gate: '-55',
    rssi_far_gate: '-85',
    upcitemdb_api_key: '',
};

function sanitizeSettingsInput(raw) {
    const sanitized = {};
    if (!raw || typeof raw !== 'object') return sanitized;

    if ('home_assistant_url' in raw) {
        sanitized.home_assistant_url = String(raw.home_assistant_url ?? '').trim();
    }
    if ('enable_ha_notifications' in raw) {
        const flag = String(raw.enable_ha_notifications).toLowerCase();
        sanitized.enable_ha_notifications = flag === 'true' ? 'true' : 'false';
    }
    if ('rssi_near_gate' in raw) {
        const n = parseInt(String(raw.rssi_near_gate), 10);
        if (!Number.isNaN(n)) {
            sanitized.rssi_near_gate = String(clamp(n, -70, -30));
        }
    }
    if ('rssi_far_gate' in raw) {
        const n = parseInt(String(raw.rssi_far_gate), 10);
        if (!Number.isNaN(n)) {
            sanitized.rssi_far_gate = String(clamp(n, -100, -71));
        }
    }
    if ('upcitemdb_api_key' in raw) {
        sanitized.upcitemdb_api_key = String(raw.upcitemdb_api_key ?? '').trim();
    }
    return sanitized;
}

function getCachedUpc(upc) {
    return new Promise((resolve, reject) => {
        db.get(
            `SELECT upc, source, name, brand, category, description, image_url, fetched_at
             FROM upc_lookup_cache WHERE upc = ?`,
            [upc],
            (err, row) => {
                if (err) reject(err);
                else resolve(row);
            }
        );
    });
}

function saveUpcCache(result) {
    return new Promise((resolve, reject) => {
        db.run(
            `INSERT OR REPLACE INTO upc_lookup_cache
             (upc, source, name, brand, category, description, image_url, raw_json)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                result.upc,
                result.source,
                result.name,
                result.brand,
                result.category,
                result.description,
                result.image_url,
                JSON.stringify(result),
            ],
            (err) => (err ? reject(err) : resolve())
        );
    });
}

function cacheRowToLookup(row) {
    return {
        found: true,
        upc: row.upc,
        source: row.source,
        cached: true,
        name: row.name,
        brand: row.brand,
        category: row.category,
        description: row.description,
        image_url: row.image_url,
        fetched_at: row.fetched_at,
    };
}

function clamp(n, min, max) {
    return Math.max(min, Math.min(max, n));
}

function maybeNotifyMisplaced(item, epc, scannedContainerId) {
    const status = computeItemStatus({
        container_id: scannedContainerId,
        home_container_id: item.home_container_id,
    });
    if (status !== 'MISPLACED') return;

    const payload = {
        event: 'item_misplaced',
        item_name: item.name,
        epc,
        current_container: scannedContainerId,
        assigned_home: normalizeContainerId(item.home_container_id),
    };

    notifyHomeAssistant(payload).catch((err) => {
        console.warn('[HA] Notification error:', err.message);
    });
}

async function notifyHomeAssistant(payload) {
    const settings = await getSystemSettings();
    if (settings.enable_ha_notifications !== 'true') return;

    const url = settings.home_assistant_url;
    if (!url) return;

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 5000);

    try {
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
            signal: controller.signal,
        });
        if (!res.ok) {
            console.warn(`[HA] Webhook responded with HTTP ${res.status}`);
        }
    } catch (err) {
        console.warn('[HA] Webhook failed:', err.message);
    } finally {
        clearTimeout(timeout);
    }
}

app.use(express.json());

/** JSONP for Windows CE Pocket IE (no XMLHttpRequest). ?callback=myCb */
function sanitizeJsonpCallback(name) {
    const s = String(name || 'cb').trim();
    if (/^[a-zA-Z_$][\w.$]{0,39}$/.test(s)) return s;
    return 'cb';
}

function sendJsonOrJsonp(req, res, payload, statusCode = 200) {
    const raw = req.query && req.query.callback;
    if (raw != null && String(raw).trim() !== '') {
        const cb = sanitizeJsonpCallback(raw);
        res.type('application/javascript; charset=utf-8');
        return res.status(statusCode).send(cb + '(' + JSON.stringify(payload) + ');');
    }
    return res.status(statusCode).json(payload);
}

function getClientIp(req) {
    const fwd = req.get('x-forwarded-for');
    if (fwd) return String(fwd).split(',')[0].trim();
    let ip = req.ip || (req.socket && req.socket.remoteAddress) || '';
    if (ip.startsWith('::ffff:')) ip = ip.slice(7);
    return ip;
}

function recordScannerHeartbeat(scannerId, req, extra = {}) {
    const id = scannerId == null ? '' : String(scannerId).trim();
    if (!id) return null;
    const prev = scannerHeartbeats.get(id) || {};
    const record = {
        scanner_id: id,
        last_seen: Date.now(),
        ip: getClientIp(req) || prev.ip || null,
        user_agent: req.get('user-agent') || prev.user_agent || null,
        battery: extra.battery != null ? extra.battery : prev.battery,
        mode: extra.mode != null ? extra.mode : prev.mode,
        app_version:
            extra.app_version != null ? extra.app_version : prev.app_version,
        live_raw:
            extra.live_raw != null ? !!extra.live_raw : prev.live_raw,
        live_scan:
            extra.live_scan != null ? !!extra.live_scan : prev.live_scan,
    };
    scannerHeartbeats.set(id, record);
    return record;
}

function listScannerPresence(now = Date.now()) {
    return [...scannerHeartbeats.entries()].map(([id, data]) => {
        const feed = getScannerLiveFeed(id);
        return {
            scanner_id: id,
            online: now - data.last_seen < SCANNER_ONLINE_THRESHOLD_MS,
            last_seen: data.last_seen,
            last_seen_ago_ms: now - data.last_seen,
            ip: data.ip,
            mode: data.mode,
            app_version: data.app_version,
            live_raw: data.live_raw,
            live_scan: data.live_scan,
            live_line_count: feed ? feed.lines.length : 0,
        };
    });
}

function getScannerPresence(scannerId, now = Date.now()) {
    const id = String(scannerId || '').trim();
    const data = scannerHeartbeats.get(id);
    const feed = getScannerLiveFeed(id);
    return {
        scanner_id: id,
        online: !!(data && now - data.last_seen < SCANNER_ONLINE_THRESHOLD_MS),
        last_seen: data ? data.last_seen : null,
        last_seen_ago_ms: data ? now - data.last_seen : null,
        ip: data ? data.ip : null,
        mode: data ? data.mode : null,
        app_version: data ? data.app_version : null,
        live_raw: data ? data.live_raw : null,
        live_scan: data ? data.live_scan : null,
        live_line_count: feed ? feed.lines.length : 0,
    };
}

const { isNewerVersion } = require('./lib/version');

const PUBLIC_DIR = path.join(__dirname, 'public');
const DEPLOY_DIR = path.join(PUBLIC_DIR, 'deploy');
const DEPLOY_CAB_NAME = 'MerlinInventoryTest.cab';
const DEPLOY_UNINSTALL_CAB_NAME = 'MerlinInventoryUninstall.cab';
const DEPLOY_STATIC_PAGES = [
    'index.html',
    'ce-wifi-test.html',
    'scanner-live.html',
    'scanner-stream-test.html',
];
const { discoverLanHosts } = require('./lib/scanner-discovery');

function deployFilesStatus() {
    const pages = {};
    for (const name of DEPLOY_STATIC_PAGES) {
        const p = path.join(DEPLOY_DIR, name);
        pages[name] = { path: p, exists: fs.existsSync(p) };
    }
    return {
        cwd: process.cwd(),
        dirname: __dirname,
        public_dir: PUBLIC_DIR,
        deploy_dir: DEPLOY_DIR,
        pages,
        cab: fs.existsSync(path.join(DEPLOY_DIR, DEPLOY_CAB_NAME)),
    };
}

let handheldVersionMeta = { version: '0.0.0+dev', assemblyVersion: '0.0.0.0' };
try {
    handheldVersionMeta = require('./version.generated.json');
} catch {
    console.warn('⚠ version.generated.json missing — run: node scripts/sync-version.js');
}

function getDeployCabMeta(filename) {
    const cabPath = path.join(DEPLOY_DIR, filename);
    if (!fs.existsSync(cabPath)) {
        return { available: false, filename };
    }
    const stat = fs.statSync(cabPath);
    return {
        available: true,
        filename,
        url: '/deploy/' + filename,
        size_kb: Math.round(stat.size / 1024),
        modified: stat.mtime.toISOString(),
    };
}

function getDeployCabInfo() {
    const install = getDeployCabMeta(DEPLOY_CAB_NAME);
    const uninstall = getDeployCabMeta(DEPLOY_UNINSTALL_CAB_NAME);
    const out = {
        cab_available: install.available,
        cab_filename: DEPLOY_CAB_NAME,
        uninstall_cab_available: uninstall.available,
        uninstall_cab_filename: DEPLOY_UNINSTALL_CAB_NAME,
    };
    if (install.available) {
        out.cab_url = install.url;
        out.cab_size_kb = install.size_kb;
        out.cab_modified = install.modified;
    }
    if (uninstall.available) {
        out.uninstall_cab_url = uninstall.url;
        out.uninstall_cab_size_kb = uninstall.size_kb;
    }
    return out;
}

// 🏓 Minimal ping (CE browser / JSONP friendly)
app.get('/api/ping', (req, res) => {
    sendJsonOrJsonp(req, res, {
        ok: true,
        server_time: Date.now(),
        version: handheldVersionMeta.version,
        message: 'inventory server reachable',
    });
});

// 📡 Live scanner stream (raw wedge + optional real-time inventory scan)
app.post('/api/scanner/live', async (req, res) => {
    const body = req.body || {};
    const scannerId = body.scanner_id == null ? '' : String(body.scanner_id).trim();
    if (!scannerId) {
        return res.status(400).json({ error: 'scanner_id is required' });
    }

    recordScannerHeartbeat(scannerId, req, {
        mode: body.mode || 'live',
        battery: body.battery ?? null,
        app_version: body.app_version,
        live_raw: true,
        live_scan: body.apply_scan === true || body.apply_scan === 'true',
    });

    let tagEntries = [];
    if (Array.isArray(body.scanned_tags)) {
        tagEntries = normalizeScannedTags(body.scanned_tags);
    } else if (typeof body.raw === 'string' && body.raw.trim()) {
        tagEntries = parseMerlinWedgeText(body.raw);
    }

    const entry = {
        mode: body.mode == null ? 'raw' : String(body.mode),
        ui_mode: body.ui_mode == null ? '' : String(body.ui_mode),
        app_version: body.app_version == null ? '' : String(body.app_version),
        raw: typeof body.raw === 'string' ? body.raw.slice(0, 12000) : '',
        tag_count: tagEntries.length,
        tags: tagsPreview(tagEntries),
    };

    const applyScan = body.apply_scan === true || body.apply_scan === 'true';
    const targetBin = normalizeContainerId(
        body.target_container_epc || body.targetContainerEpc || null
    );

    if (applyScan && tagEntries.length > 0) {
        try {
            const settings = await getSystemSettingsCached();
            recordNearFieldReads(tagEntries, settings);
            entry.scan_result = await runInventoryScan(targetBin, tagEntries, {
                source: 'scanner-live',
                scanner_id: scannerId,
            });
        } catch (err) {
            entry.scan_error = err.message;
        }
    }

    const saved = appendScannerLiveLine(scannerId, entry);
    res.json({
        ok: true,
        scanner_id: scannerId,
        line_id: saved ? saved.id : null,
        tag_count: tagEntries.length,
    });
});

app.get('/api/scanner/live', (req, res) => {
    const scannerId = req.query.scanner_id == null ? '' : String(req.query.scanner_id).trim();
    if (!scannerId) {
        return sendJsonOrJsonp(req, res, { error: 'scanner_id query required' }, 400);
    }
    const since = parseInt(req.query.since_id, 10) || 0;
    const feed = getScannerLiveFeed(scannerId);
    const lines = feed ? feed.lines.filter((line) => line.id > since) : [];
    const latestId = feed && feed.lines.length ? feed.lines[feed.lines.length - 1].id : 0;
    const presence = getScannerPresence(scannerId);
    const allScanners = listScannerPresence();
    const payload = {
        ok: true,
        scanner_id: scannerId,
        since_id: since,
        latest_id: latestId,
        lines,
        server_time: Date.now(),
        presence,
        online_threshold_ms: SCANNER_ONLINE_THRESHOLD_MS,
        known_scanners: allScanners,
    };
    if (req.query.include_diag === '1' && feed) {
        payload.diag_log = feed.diagLog || '';
        payload.diag_log_bytes = payload.diag_log.length;
        payload.diag_log_updated = feed.diagUpdated || 0;
    }
    sendJsonOrJsonp(req, res, payload);
});

app.get('/api/scanner/discover', async (req, res) => {
    const now = Date.now();
    const scanners = listScannerPresence(now);
    const online = scanners.filter((s) => s.online);
    let suggested =
        req.query.scanner_id != null ? String(req.query.scanner_id).trim() : '';
    if (!suggested && online.length === 1) suggested = online[0].scanner_id;
    if (!suggested && online.length > 0) suggested = online[0].scanner_id;

    let lan = { subnet: null, hosts: [], cached: false, note: null };
    if (req.query.scan === '1') {
        try {
            const serverIp = getClientIp(req) || '127.0.0.1';
            lan = await discoverLanHosts(serverIp, req.query.force === '1');
            for (const host of lan.hosts) {
                const match = scanners.find((s) => s.ip === host && s.online);
                if (match) match.lan_reachable = true;
            }
        } catch (err) {
            lan.note = err.message;
        }
    }

    sendJsonOrJsonp(req, res, {
        ok: true,
        server_time: now,
        scanners,
        suggested_scanner_id: suggested || null,
        any_online: online.length > 0,
        lan,
    });
});

app.post('/api/scanner/live/clear', (req, res) => {
    const scannerId =
        (req.body && req.body.scanner_id) || req.query.scanner_id || '';
    const id = String(scannerId).trim();
    if (!id) return res.status(400).json({ error: 'scanner_id required' });
    const feed = getScannerLiveFeed(id);
    if (feed) {
        feed.lines = [];
        feed.nextId = 1;
    }
    res.json({ ok: true, scanner_id: id });
});

function appendStreamEvent(scannerId, entry) {
    const feed = getScannerLiveFeed(scannerId);
    if (!feed) return null;
    if (!feed.streamEvents) feed.streamEvents = [];
    feed.streamNextId = feed.streamNextId || 1;
    entry.id = feed.streamNextId++;
    entry.t = entry.t || Date.now();
    feed.streamEvents.push(entry);
    while (feed.streamEvents.length > SCANNER_STREAM_MAX_EVENTS) {
        feed.streamEvents.shift();
    }
    feed.streamUpdated = Date.now();
    return entry;
}

/** Direct stream from MerlinStreamTest — no local log on gun. */
app.post('/api/scanner/stream/event', (req, res) => {
    const body = req.body || {};
    const scannerId = body.scanner_id == null ? '' : String(body.scanner_id).trim();
    if (!scannerId) {
        return res.status(400).json({ error: 'scanner_id is required' });
    }

    recordScannerHeartbeat(scannerId, req, {
        mode: body.screen || body.event_type || 'stream',
        app_version: body.app_version,
        live_raw: true,
    });

    const tagList = Array.isArray(body.tags) ? body.tags : [];
    const saved = appendStreamEvent(scannerId, {
        type: body.event_type || 'event',
        screen: body.screen || null,
        action: body.action || null,
        source: body.source || null,
        raw: body.raw == null ? '' : String(body.raw),
        tag_count: body.tag_count != null ? body.tag_count : tagList.length,
        tags: tagList,
        http_ok: body.http_ok,
        http_error: body.http_error || null,
        app_version: body.app_version || null,
    });

    if (body.announce === true || body.announce === 'true') {
        appendScannerLiveLine(scannerId, {
            mode: 'session',
            ui_mode: body.screen,
            app_version: body.app_version,
            raw: 'Stream: ' + (body.event_type || 'event'),
            tag_count: 0,
            tags: [],
        });
    }

    res.json({
        ok: true,
        scanner_id: scannerId,
        event_id: saved ? saved.id : null,
        stream_bytes: getScannerLiveFeed(scannerId).streamEvents.length,
    });
});

app.get('/api/scanner/stream', (req, res) => {
    const scannerId = req.query.scanner_id == null ? '' : String(req.query.scanner_id).trim();
    if (!scannerId) {
        return sendJsonOrJsonp(req, res, { error: 'scanner_id query required' }, 400);
    }
    const since = parseInt(req.query.since_id, 10) || 0;
    const feed = getScannerLiveFeed(scannerId);
    const events =
        feed && feed.streamEvents
            ? feed.streamEvents.filter((ev) => ev.id > since)
            : [];
    const latestId =
        feed && feed.streamEvents && feed.streamEvents.length
            ? feed.streamEvents[feed.streamEvents.length - 1].id
            : 0;
    sendJsonOrJsonp(req, res, {
        ok: true,
        scanner_id: scannerId,
        since_id: since,
        latest_id: latestId,
        events,
        server_time: Date.now(),
        stream_updated: feed ? feed.streamUpdated : 0,
        presence: getScannerPresence(scannerId),
    });
});

app.post('/api/scanner/stream/clear', (req, res) => {
    const scannerId =
        (req.body && req.body.scanner_id) || req.query.scanner_id || '';
    const id = String(scannerId).trim();
    if (!id) return res.status(400).json({ error: 'scanner_id required' });
    const feed = getScannerLiveFeed(id);
    if (feed) {
        feed.streamEvents = [];
        feed.streamNextId = 1;
        feed.streamUpdated = Date.now();
    }
    res.json({ ok: true, scanner_id: id });
});

// 📝 Handheld diagnostic log upload (download in browser before new CAB)
app.post('/api/handheld/diagnostic-log', (req, res) => {
    const body = req.body || {};
    const scannerId = body.scanner_id == null ? '' : String(body.scanner_id).trim();
    if (!scannerId) {
        return res.status(400).json({ error: 'scanner_id is required' });
    }
    const feed = getScannerLiveFeed(scannerId);
    if (!feed) return res.status(400).json({ error: 'invalid scanner_id' });

    if (body.reset === true || body.reset === 'true') {
        feed.diagLog = '';
        feed.diagUpdated = Date.now();
        return res.json({ ok: true, scanner_id: scannerId, bytes: 0 });
    }

    const chunk = body.append == null ? '' : String(body.append);
    if (chunk) {
        feed.diagLog = (feed.diagLog + chunk).slice(-SCANNER_DIAG_LOG_MAX);
        feed.diagUpdated = Date.now();
    }
    res.json({
        ok: true,
        scanner_id: scannerId,
        bytes: feed.diagLog.length,
        updated: feed.diagUpdated,
    });
});

app.get('/api/handheld/diagnostic-log', (req, res) => {
    const scannerId = req.query.scanner_id == null ? '' : String(req.query.scanner_id).trim();
    if (!scannerId) {
        return sendJsonOrJsonp(req, res, { error: 'scanner_id query required' }, 400);
    }
    const feed = getScannerLiveFeed(scannerId);
    const logText = feed ? feed.diagLog : '';

    if (req.query.download === '1' || req.query.download === 'true') {
        res.type('text/plain; charset=utf-8');
        res.setHeader(
            'Content-Disposition',
            'attachment; filename="merlin-' + scannerId + '-debug.log"'
        );
        return res.send(logText || '(empty log)\n');
    }

    sendJsonOrJsonp(req, res, {
        ok: true,
        scanner_id: scannerId,
        bytes: logText.length,
        updated: feed ? feed.diagUpdated : 0,
        log: logText,
        download_url:
            '/api/handheld/diagnostic-log?scanner_id=' +
            encodeURIComponent(scannerId) +
            '&download=1',
    });
});

// 📦 Handheld / deploy hub — Wi‑Fi CAB update metadata
app.get('/api/deploy/info', (req, res) => {
    const clientVersion =
        req.query.client_version == null ? '' : String(req.query.client_version).trim();
    const serverVersion = handheldVersionMeta.version;
    sendJsonOrJsonp(req, res, {
        app: 'MerlinInventoryTest',
        version: serverVersion,
        assembly_version: handheldVersionMeta.assemblyVersion,
        git_commit: handheldVersionMeta.gitCommit,
        built_at: handheldVersionMeta.builtAt,
        server_time: Date.now(),
        deploy_page: '/deploy/',
        wifi_test_page: '/deploy/ce-wifi-test.html',
        scanner_live_page: '/deploy/scanner-live.html',
        update_available: clientVersion ? isNewerVersion(serverVersion, clientVersion) : false,
        client_version: clientVersion || null,
        ...getDeployCabInfo(),
    });
});

// 📡 GET heartbeat for legacy CE browsers (use instead of POST JSON)
app.get('/api/scanner/ping', (req, res) => {
    const scannerId = req.query.scanner_id == null ? '' : String(req.query.scanner_id).trim();
    if (!scannerId) {
        return sendJsonOrJsonp(req, res, { error: 'scanner_id query required' }, 400);
    }
    recordScannerHeartbeat(scannerId, req, {
        mode: req.query.mode || 'ping',
        app_version: req.query.app_version,
        live_raw: req.query.live_raw === '1' || req.query.live_raw === 'true',
        live_scan: req.query.live_scan === '1' || req.query.live_scan === 'true',
    });
    sendJsonOrJsonp(req, res, {
        status: 'ok',
        scanner_id: scannerId,
        online: true,
        presence: getScannerPresence(scannerId),
    });
});

// 📱 Lightweight sync check (small JSON for CE)
app.get('/api/handheld/sync-summary', async (req, res) => {
    try {
        const [itemRow, binRow, settings] = await Promise.all([
            dbGet(`SELECT COUNT(*) AS n FROM items`),
            dbGet(`SELECT COUNT(*) AS n FROM containers`),
            getSystemSettingsCached(),
        ]);
        const nearGate = parseInt(settings.rssi_near_gate, 10) || -55;
        const farGate = parseInt(settings.rssi_far_gate, 10) || -85;
        sendJsonOrJsonp(req, res, {
            ok: true,
            synced_at: Date.now(),
            item_count: itemRow ? itemRow.n : 0,
            bin_count: binRow ? binRow.n : 0,
            hunt_targets: activeSearchQueue.length,
            activeSearchQueue: [...activeSearchQueue],
            revision: huntRevision,
            hunt_targets_detail: buildHuntTargetsForQueue(nearGate, farGate),
            rssi_near_gate: nearGate,
            rssi_far_gate: farGate,
        });
    } catch (err) {
        sendJsonOrJsonp(req, res, { ok: false, error: err.message }, 500);
    }
});

// Explicit CAB download (some Windows CE browsers need octet-stream)
function sendDeployCab(res, filename) {
    const cabPath = path.join(DEPLOY_DIR, filename);
    if (!fs.existsSync(cabPath)) {
        return res.status(404).type('text/plain').send('CAB not found on server');
    }
    res.setHeader('Content-Type', 'application/octet-stream');
    res.setHeader('Content-Disposition', 'attachment; filename="' + filename + '"');
    return res.sendFile(cabPath);
}

app.get('/deploy/' + DEPLOY_CAB_NAME, (req, res) => {
    sendDeployCab(res, DEPLOY_CAB_NAME);
});

app.get('/deploy/' + DEPLOY_UNINSTALL_CAB_NAME, (req, res) => {
    sendDeployCab(res, DEPLOY_UNINSTALL_CAB_NAME);
});

function sendDeployStaticPage(res, filename, req) {
    const pagePath = path.resolve(path.join(DEPLOY_DIR, filename));
    if (!fs.existsSync(pagePath)) {
        return res
            .status(404)
            .type('text/html')
            .send(
                '<h1>Not on server yet</h1>' +
                    '<p><code>' +
                    filename +
                    '</code> is missing at:</p><p><code>' +
                    pagePath +
                    '</code></p>' +
                    '<p>Run <code>git pull</code> in the app work-tree and <code>pm2 restart rfid-brain</code>, or <code>scp</code> the file to <code>public/deploy/</code>.</p>' +
                    '<p><a href="/deploy/">Deploy hub</a> · <a href="/api/deploy/health">Deploy health JSON</a></p>'
            );
    }
    return res.sendFile(pagePath);
}

for (const pageName of DEPLOY_STATIC_PAGES) {
    const route = '/deploy/' + pageName;
    app.get(route, (req, res) => {
        sendDeployStaticPage(res, pageName, req);
    });
}

app.get('/api/deploy/health', (req, res) => {
    const status = deployFilesStatus();
    status.server_version = handheldVersionMeta.version;
    status.scanner_live_url = '/deploy/scanner-live.html';
    sendJsonOrJsonp(req, res, status);
});

// Serves index.html, mobile.html, emulator.html, and /public assets — no route conflict with /api/*
app.use(express.static(PUBLIC_DIR));

function normalizeScanTag(epc) {
    return epc == null ? '' : String(epc).trim();
}

function parseScannedTagEntry(entry) {
    if (typeof entry === 'string') {
        return { epc: normalizeScanTag(entry), rssi: null };
    }
    if (entry && typeof entry === 'object') {
        const rawRssi = entry.rssi ?? entry.RSSI ?? entry.signal;
        const rssi = rawRssi == null ? null : parseInt(String(rawRssi), 10);
        return {
            epc: normalizeScanTag(entry.epc ?? entry.EPC ?? entry.id ?? entry.tag),
            rssi: Number.isNaN(rssi) ? null : rssi,
        };
    }
    return { epc: '', rssi: null };
}

function normalizeScannedTags(rawTags) {
    if (!Array.isArray(rawTags)) return [];
    return rawTags.map(parseScannedTagEntry).filter((t) => t.epc);
}

function invalidateKnownEpcRegistryCache() {
    knownEpcRegistryCache = null;
    knownEpcRegistryCacheAt = 0;
}

function loadKnownEpcRegistry() {
    const now = Date.now();
    if (knownEpcRegistryCache && now - knownEpcRegistryCacheAt < KNOWN_EPC_CACHE_MS) {
        return Promise.resolve(knownEpcRegistryCache);
    }

    return new Promise((resolve, reject) => {
        db.all(`SELECT epc_id FROM items`, [], (itemErr, itemRows) => {
            if (itemErr) return reject(itemErr);
            db.all(
                `SELECT id, boundary_tag_a, boundary_tag_b FROM containers`,
                [],
                (binErr, binRows) => {
                    if (binErr) return reject(binErr);
                    const items = new Set();
                    const binIds = new Set();
                    const boundaries = new Set();
                    (itemRows || []).forEach((row) => {
                        const id = normalizeScanTag(row.epc_id);
                        if (id) items.add(id.toLowerCase());
                    });
                    (binRows || []).forEach((row) => {
                        const id = normalizeScanTag(row.id);
                        if (id) binIds.add(id.toLowerCase());
                        const a = normalizeScanTag(row.boundary_tag_a);
                        const b = normalizeScanTag(row.boundary_tag_b);
                        if (a) boundaries.add(a.toLowerCase());
                        if (b) boundaries.add(b.toLowerCase());
                    });
                    knownEpcRegistryCache = { items, binIds, boundaries };
                    knownEpcRegistryCacheAt = now;
                    resolve(knownEpcRegistryCache);
                }
            );
        });
    });
}

function classifyEpcAgainstRegistry(epc, registry) {
    const key = normalizeScanTag(epc);
    if (!key || !registry) return 'UNREGISTERED';
    const lower = key.toLowerCase();
    if (registry.items.has(lower)) return 'REGISTERED_ITEM';
    if (registry.boundaries.has(lower)) return 'BOUNDARY_TAG';
    if (registry.binIds.has(lower)) return 'BIN_CONTAINER_ID';
    return 'UNREGISTERED';
}

function touchRecentHardwareRead(epc, rssi) {
    const normalized = normalizeScanTag(epc);
    if (!normalized) return;
    recentHardwareReads.set(normalized, {
        epc: normalized,
        rssi: rssi == null || Number.isNaN(rssi) ? null : rssi,
        last_seen: Date.now(),
    });
    const cutoff = Date.now() - RECENT_HARDWARE_READ_TTL_MS;
    recentHardwareReads.forEach((row, key) => {
        if (row.last_seen < cutoff) recentHardwareReads.delete(key);
    });
}

function appendDiagnosticsIngress(tagEntries, meta) {
    if (!tagEntries || !tagEntries.length) return;

    loadKnownEpcRegistry()
        .then((registry) => {
            const now = Date.now();
            tagEntries.forEach(({ epc, rssi }) => {
                if (!epc) return;
                touchRecentHardwareRead(epc, rssi);
                diagnosticsLiveBuffer.unshift({
                    id: `${now}-${epc}-${Math.random().toString(36).slice(2, 8)}`,
                    timestamp: new Date(now).toISOString(),
                    epc: normalizeScanTag(epc),
                    rssi: rssi == null || Number.isNaN(rssi) ? null : rssi,
                    classification: classifyEpcAgainstRegistry(epc, registry),
                    source: meta.source || 'hardware',
                    target_container_epc: meta.targetContainerEpc || null,
                    spatial_zone: meta.spatial_zone || null,
                });
            });
            while (diagnosticsLiveBuffer.length > DIAGNOSTICS_BUFFER_MAX) {
                diagnosticsLiveBuffer.pop();
            }
            if (meta.spatial_zone && meta.spatial_zone.container_id) {
                spatialHighlightUntil.set(
                    meta.spatial_zone.container_id,
                    now + SPATIAL_HIGHLIGHT_MS
                );
            }
        })
        .catch((err) => {
            console.warn('[diagnostics] ingress:', err.message);
        });
}

function recordNearFieldReads(tagEntries, settings) {
    const nearGate = parseInt(settings.rssi_near_gate, 10) || -55;
    const now = Date.now();
    let recorded = 0;
    let huntUpdated = false;

    tagEntries.forEach(({ epc, rssi }) => {
        if (!epc) return;
        if (rssi != null && !Number.isNaN(rssi)) {
            touchRecentHardwareRead(epc, rssi);
        }
        if (rssi == null || Number.isNaN(rssi)) return;

        const normalized = normalizeScanTag(epc);
        const isHuntTarget = activeSearchQueue.some((id) => epcEquals(id, normalized));
        if (isHuntTarget) {
            const prev = huntRssiByEpc.get(normalized);
            huntRssiByEpc.set(normalized, { epc: normalized, rssi, timestamp: now });
            if (!prev || prev.rssi !== rssi || now - prev.timestamp >= 40) {
                huntUpdated = true;
            }
        }

        if (rssi >= nearGate) {
            nearFieldBuffer.unshift({ epc: normalized, rssi, timestamp: now });
            recorded += 1;
        }
    });

    while (nearFieldBuffer.length > NEAR_FIELD_BUFFER_MAX) {
        nearFieldBuffer.pop();
    }

    if (recorded > 0) {
        console.log(`📡 Near-field buffer: +${recorded} ultra-near read(s) (gate ≥ ${nearGate} dBm)`);
    }

    if (huntUpdated && activeSearchQueue.length > 0) {
        bumpHuntRevision();
    }

    return huntUpdated;
}

function removeLongPollWaiter(entry) {
    const idx = huntLongPollWaiters.indexOf(entry);
    if (idx >= 0) huntLongPollWaiters.splice(idx, 1);
}

function registerLongPollWaiter(req, res, timeoutMs) {
    const entry = { res, timer: null };
    entry.timer = setTimeout(() => {
        removeLongPollWaiter(entry);
        if (res.headersSent) return;
        buildSearchTargetPayload()
            .then((payload) => res.json({ ...payload, long_poll: 'timeout' }))
            .catch((err) => res.status(500).json({ error: err.message }));
    }, timeoutMs);

    req.on('close', () => {
        clearTimeout(entry.timer);
        removeLongPollWaiter(entry);
    });

    huntLongPollWaiters.push(entry);
}

function flushHuntLongPollWaiters(payload) {
    if (!huntLongPollWaiters.length) return;
    const waiters = huntLongPollWaiters.slice();
    huntLongPollWaiters.length = 0;
    waiters.forEach((entry) => {
        clearTimeout(entry.timer);
        if (!entry.res.headersSent) {
            entry.res.json(payload);
        }
    });
}

function notifyHuntClients() {
    buildSearchTargetPayload()
        .then((payload) => {
            const json = JSON.stringify(payload);
            huntWsClients.forEach((ws) => {
                if (ws.readyState === WebSocket.OPEN) {
                    try {
                        ws.send(json);
                    } catch (_) {
                        huntWsClients.delete(ws);
                    }
                }
            });
            huntSseClients.forEach((client) => {
                try {
                    client.res.write(`data: ${json}\n\n`);
                } catch (_) {
                    huntSseClients.delete(client);
                }
            });
            flushHuntLongPollWaiters(payload);
        })
        .catch((err) => console.error('Hunt notify failed:', err.message));
}

function bumpHuntRevision() {
    huntRevision += 1;
    notifyHuntClients();
}

function rssiZoneLabel(rssi, nearGate, farGate) {
    if (rssi == null || Number.isNaN(rssi)) return 'NO_SIGNAL';
    if (rssi >= nearGate) return 'CLOSE';
    if (rssi >= farGate) return 'WARM';
    return 'COLD';
}

function buildHuntSignal(primaryEpc, nearGate, farGate) {
    if (!primaryEpc) return null;
    const normalized = normalizeScanTag(primaryEpc);
    const read = huntRssiByEpc.get(normalized);
    const now = Date.now();

    if (!read) {
        return {
            epc: primaryEpc,
            rssi: null,
            zone: 'NO_SIGNAL',
            message: 'Pull trigger on the Merlin — waiting for RFID read with RSSI.',
            stale: false,
        };
    }

    const ageMs = now - read.timestamp;
    return {
        epc: read.epc,
        rssi: read.rssi,
        timestamp: read.timestamp,
        age_ms: ageMs,
        zone: rssiZoneLabel(read.rssi, nearGate, farGate),
        stale: ageMs > HUNT_SIGNAL_STALE_MS,
        message:
            ageMs > HUNT_SIGNAL_STALE_MS
                ? 'Last read is stale — pull trigger again.'
                : null,
    };
}

function buildHuntTargetsForQueue(nearGate, farGate) {
    return activeSearchQueue.map((epc) => buildHuntSignal(epc, nearGate, farGate));
}

async function buildSearchTargetPayload() {
    const settings = await getSystemSettings();
    const nearGate = parseInt(settings.rssi_near_gate, 10) || -55;
    const farGate = parseInt(settings.rssi_far_gate, 10) || -85;
    const primary = activeSearchQueue[0] || null;
    const hunt_targets = buildHuntTargetsForQueue(nearGate, farGate);

    return {
        activeSearchQueue,
        revision: huntRevision,
        hunt_signal: buildHuntSignal(primary, nearGate, farGate),
        hunt_targets,
        rssi_near_gate: nearGate,
        rssi_far_gate: farGate,
        scanner_model: 'Nordic ID Merlin HTE00072',
    };
}

function findLatestUnassignedNearField(sinceMs, nearGate) {
    const candidates = nearFieldBuffer.filter(
        (read) => read.timestamp > sinceMs && read.rssi >= nearGate
    );
    if (!candidates.length) return null;

    candidates.sort((a, b) => b.rssi - a.rssi || b.timestamp - a.timestamp);
    return candidates[0];
}

function parseMerlinWedgeText(text) {
    const raw = text == null ? '' : String(text).trim();
    if (!raw) return [];

    return raw
        .split(/[\r\n,;]+/)
        .map((part) => part.trim())
        .filter(Boolean)
        .map((part) => {
            if (part.indexOf('|') >= 0) {
                const pieces = part.split('|');
                return parseScannedTagEntry({ epc: pieces[0], rssi: pieces[1] });
            }
            if (part.indexOf('\t') >= 0) {
                const pieces = part.split('\t');
                return parseScannedTagEntry({ epc: pieces[0], rssi: pieces[1] });
            }
            return parseScannedTagEntry(part);
        })
        .filter((t) => t.epc);
}

function parseMerlinWedgePayload(body) {
    const meta = {
        targetContainerEpc: null,
        scanner_id: 'MERLIN-WEDGE',
        tagEntries: [],
    };

    if (body == null) return meta;

    if (typeof body === 'string') {
        meta.tagEntries = parseMerlinWedgeText(body);
        return meta;
    }

    if (typeof body !== 'object') return meta;

    meta.targetContainerEpc = normalizeContainerId(
        body.target_container_epc || body.targetContainerEpc || body.bin_id || body.bin
    );
    meta.scanner_id =
        body.scanner_id == null ? 'MERLIN-WEDGE' : String(body.scanner_id).trim() || 'MERLIN-WEDGE';

    if (typeof body.raw === 'string' && body.raw.trim()) {
        meta.tagEntries = parseMerlinWedgeText(body.raw);
        return meta;
    }
    if (typeof body.text === 'string' && body.text.trim()) {
        meta.tagEntries = parseMerlinWedgeText(body.text);
        return meta;
    }
    if (typeof body.data === 'string' && body.data.trim()) {
        meta.tagEntries = parseMerlinWedgeText(body.data);
        return meta;
    }
    if (typeof body.tags === 'string' && body.tags.trim()) {
        meta.tagEntries = parseMerlinWedgeText(body.tags);
        return meta;
    }

    const scanned = body.scanned_tags || body.tags;
    if (Array.isArray(scanned)) {
        meta.tagEntries = normalizeScannedTags(scanned);
    }

    return meta;
}

function runInventoryScan(targetContainerEpc, tagEntries, scanMeta = {}) {
    const normalizedTags = tagEntries.map((t) => t.epc);

    return new Promise((resolve, reject) => {
        db.all(
            `SELECT id, name, boundary_tag_a, boundary_tag_b FROM containers
             WHERE TRIM(COALESCE(boundary_tag_a, '')) != ''
               AND TRIM(COALESCE(boundary_tag_b, '')) != ''`,
            [],
            (err, boundaryContainers) => {
                if (err) return reject(err);

                let effectiveTarget = targetContainerEpc;
                let tagsToProcess = normalizedTags;

                const zoneMatch = findSpatialZoneMatch(normalizedTags, boundaryContainers);
                let spatialZonePayload = null;
                if (zoneMatch) {
                    const { container, tagA, tagB } = zoneMatch;
                    effectiveTarget = container.id;
                    const boundarySet = new Set([tagA, tagB]);
                    tagsToProcess = normalizedTags.filter((epc) => !boundarySet.has(epc));
                    spatialZonePayload = {
                        container_id: container.id,
                        container_name: container.name,
                        boundary_tag_a: tagA,
                        boundary_tag_b: tagB,
                        items_isolated: tagsToProcess.length,
                    };

                    console.log(
                        `🎯 Spatial Zone Match: Isolated ${tagsToProcess.length} items between boundaries of bin [${container.name}]`
                    );
                }

                appendDiagnosticsIngress(tagEntries, {
                    source: scanMeta.source || 'inventory-scan',
                    targetContainerEpc: effectiveTarget,
                    spatial_zone: spatialZonePayload,
                });

                processScannedTags(tagsToProcess, effectiveTarget);

                resolve({
                    status: 'success',
                    message: `Database processed ${tagsToProcess.length} tags.`,
                    tags_received: normalizedTags.length,
                    tags_processed: tagsToProcess.length,
                    target_container_epc: effectiveTarget,
                    spatial_zone: zoneMatch
                        ? {
                              container_id: zoneMatch.container.id,
                              container_name: zoneMatch.container.name,
                          }
                        : null,
                });
            }
        );
    });
}

function findSpatialZoneMatch(scannedTags, containers) {
    const tagSet = new Set(scannedTags.map(normalizeScanTag).filter(Boolean));

    for (const container of containers) {
        const tagA = normalizeScanTag(container.boundary_tag_a);
        const tagB = normalizeScanTag(container.boundary_tag_b);
        if (!tagA || !tagB) continue;
        if (tagSet.has(tagA) && tagSet.has(tagB)) {
            return { container, tagA, tagB };
        }
    }
    return null;
}

async function processScannedTags(scannedTags, targetContainerEpc) {
    if (!scannedTags.length) return;

    const uniqueTags = [...new Set(scannedTags.map((t) => normalizeScanTag(t)).filter(Boolean))];
    if (!uniqueTags.length) return;

    const lowerTags = uniqueTags.map((t) => t.toLowerCase());
    const placeholders = lowerTags.map(() => '?').join(',');

    let rows = [];
    try {
        rows = await dbAll(
            `SELECT epc_id, name, container_id, home_container_id FROM items
             WHERE LOWER(epc_id) IN (${placeholders})`,
            lowerTags
        );
    } catch (err) {
        console.error('processScannedTags batch load:', err);
        return;
    }

    const itemByLowerEpc = new Map(rows.map((row) => [row.epc_id.toLowerCase(), row]));
    const unknownTags = uniqueTags.filter((epc) => !itemByLowerEpc.has(epc.toLowerCase()));

    for (const epc of uniqueTags) {
        const item = itemByLowerEpc.get(epc.toLowerCase());
        if (!item) continue;

        if (item.container_id !== targetContainerEpc) {
            console.log(`📦 MOVED: '${item.name}' [${epc}] moved to bin [${targetContainerEpc}]`);
            await dbRun(`UPDATE items SET container_id = ? WHERE epc_id = ?`, [
                targetContainerEpc,
                item.epc_id,
            ]);
            await dbRun(
                `INSERT INTO scan_history (scanned_epc, parent_container_epc, action) VALUES (?, ?, ?)`,
                [item.epc_id, targetContainerEpc, 'MOVED']
            );
            maybeNotifyMisplaced(item, item.epc_id, targetContainerEpc);
        } else {
            console.log(`🎯 CONFIRMED: '${item.name}' is still in bin [${targetContainerEpc}]`);
            await dbRun(
                `INSERT INTO scan_history (scanned_epc, parent_container_epc, action) VALUES (?, ?, ?)`,
                [item.epc_id, targetContainerEpc, 'FOUND']
            );
            maybeNotifyMisplaced(item, item.epc_id, targetContainerEpc);
        }
    }

    for (const epc of unknownTags) {
        try {
            const regResult = await checkEpcForRoleAsync(epc, { role: 'item' });
            if (!regResult.valid) {
                console.log(`⚠️ Skipped auto-register [${epc}]: ${regResult.message}`);
                await dbRun(
                    `INSERT INTO scan_history (scanned_epc, parent_container_epc, action) VALUES (?, ?, ?)`,
                    [epc, targetContainerEpc, 'REJECTED']
                );
                continue;
            }
            console.log(`🆕 UNKNOWN TAG DETECTED: [${epc}]. Creating placeholder entry.`);
            await dbRun(
                `INSERT INTO items (epc_id, name, container_id) VALUES (?, ?, ?)`,
                [epc, `Unknown RFID Tag (${epc.slice(-4)})`, targetContainerEpc]
            );
            await dbRun(
                `INSERT INTO scan_history (scanned_epc, parent_container_epc, action) VALUES (?, ?, ?)`,
                [epc, targetContainerEpc, 'REGISTERED']
            );
        } catch (err) {
            console.error(`processScannedTags unknown [${epc}]:`, err);
        }
    }
}

// 🧪 Spatial diagnostics live feed (poll from diagnostics.html)
app.get('/api/diagnostics/live', async (req, res) => {
    try {
        const containers = await dbAllAsync(
            `SELECT id, name, description, boundary_tag_a, boundary_tag_b FROM containers ORDER BY name ASC`
        );
        const now = Date.now();
        const spatial_highlights = {};
        spatialHighlightUntil.forEach((expiresAt, containerId) => {
            if (expiresAt > now) spatial_highlights[containerId] = expiresAt;
            else spatialHighlightUntil.delete(containerId);
        });

        const scannerStatus = await new Promise((resolve) => {
            getSystemSettings()
                .then(() => {
                    const scanners = [...scannerHeartbeats.entries()].map(([id, data]) => ({
                        scanner_id: id,
                        online: now - data.last_seen < SCANNER_ONLINE_THRESHOLD_MS,
                    }));
                    resolve(scanners.some((s) => s.online));
                })
                .catch(() => resolve(false));
        });

        res.json({
            reads: diagnosticsLiveBuffer.slice(0, 80),
            containers,
            spatial_highlights,
            scanner_online: scannerStatus,
            ingress_note:
                'Fed by POST /api/hardware/merlin-wedge and POST /api/scan only (no background mock loop).',
        });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 📥 Export diagnostic logs for AI / filter tuning
app.get('/api/diagnostics/export', async (req, res) => {
    try {
        const [history, items, containers] = await Promise.all([
            dbAllAsync(
                `SELECT id, timestamp, scanned_epc, parent_container_epc, action
                 FROM scan_history ORDER BY timestamp DESC LIMIT 200`
            ),
            dbAllAsync(
                `SELECT epc_id, name, description, category, container_id, home_container_id, upc
                 FROM items`
            ),
            dbAllAsync(
                `SELECT id, name, description, boundary_tag_a, boundary_tag_b FROM containers`
            ),
        ]);

        const itemByEpc = new Map(
            items.map((row) => [String(row.epc_id).toLowerCase(), row])
        );
        const containerById = new Map(
            containers.map((row) => [String(row.id).toLowerCase(), row])
        );

        const boundary_cross_references = containers
            .filter(
                (c) =>
                    String(c.boundary_tag_a || '').trim() &&
                    String(c.boundary_tag_b || '').trim()
            )
            .map((c) => ({
                container_id: c.id,
                container_name: c.name,
                boundary_tag_a: c.boundary_tag_a,
                boundary_tag_b: c.boundary_tag_b,
                sandwich_rule:
                    'Both boundary EPCs present in same scan batch ⇒ items assigned to this bin',
            }));

        const enriched_history = history.map((row) => {
            const epcKey = String(row.scanned_epc || '').toLowerCase();
            const parentKey = String(row.parent_container_epc || '').toLowerCase();
            const item = itemByEpc.get(epcKey) || null;
            const parentBin = containerById.get(parentKey) || null;
            let tag_role = 'unknown';
            if (item) tag_role = 'registered_item';
            else if (
                containers.some(
                    (c) =>
                        epcEquals(c.boundary_tag_a, row.scanned_epc) ||
                        epcEquals(c.boundary_tag_b, row.scanned_epc)
                )
            ) {
                tag_role = 'boundary_tag';
            } else if (containerById.has(epcKey)) {
                tag_role = 'bin_container_id';
            } else {
                tag_role = 'unregistered';
            }
            return {
                ...row,
                tag_role,
                item_association: item,
                parent_bin: parentBin,
            };
        });

        const payload = {
            exported_at: new Date().toISOString(),
            format: 'rfid-inventory-diagnostics-v1',
            scan_history: enriched_history,
            items_snapshot: items,
            containers_snapshot: containers,
            boundary_cross_references,
            live_buffer_recent: diagnosticsLiveBuffer.slice(0, 50),
            recent_hardware_reads: [...recentHardwareReads.values()].sort(
                (a, b) => b.last_seen - a.last_seen
            ),
        };

        const filename =
            'rfid-diagnostics-' +
            new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19) +
            '.json';

        res.setHeader('Content-Type', 'application/json; charset=utf-8');
        res.setHeader('Content-Disposition', `attachment; filename="${filename}"`);
        res.send(JSON.stringify(payload, null, 2));
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 🏴 Rogue / unassigned tags seen on hardware ingress (not in items or containers registry)
app.get('/api/scan/unassigned', async (req, res) => {
    try {
        const registry = await loadKnownEpcRegistry();
        const now = Date.now();
        const cutoff = now - RECENT_HARDWARE_READ_TTL_MS;

        const rows = [...recentHardwareReads.values()]
            .filter((row) => row.last_seen >= cutoff)
            .filter((row) => classifyEpcAgainstRegistry(row.epc, registry) === 'UNREGISTERED')
            .sort((a, b) => b.last_seen - a.last_seen)
            .map((row) => ({
                epc: row.epc,
                rssi: row.rssi,
                last_seen: new Date(row.last_seen).toISOString(),
                last_seen_ms: row.last_seen,
            }));

        res.json({
            unassigned: rows,
            ttl_ms: RECENT_HARDWARE_READ_TTL_MS,
            ingress_note:
                'Tags from merlin-wedge / api-scan only. Register via Claim & Register.',
        });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 📡 Scanner connectivity heartbeat (Merlin handheld / hardware client)
app.post('/api/scanner/heartbeat', (req, res) => {
    const body = req.body || {};
    const scannerId = body.scanner_id == null ? '' : String(body.scanner_id).trim();
    if (!scannerId) {
        return res.status(400).json({ error: 'scanner_id is required' });
    }

    const record = recordScannerHeartbeat(scannerId, req, {
        battery: body.battery ?? null,
        mode: body.mode ?? null,
        app_version: body.app_version,
        live_raw:
            body.live_raw != null
                ? body.live_raw === true || body.live_raw === 'true'
                : undefined,
        live_scan:
            body.live_scan != null
                ? body.live_scan === true || body.live_scan === 'true'
                : undefined,
    });

    if (body.announce === true || body.announce === 'true') {
        appendScannerLiveLine(scannerId, {
            mode: 'session',
            ui_mode: body.mode || 'heartbeat',
            app_version: body.app_version || '',
            raw: 'Scanner connected — live stream ' +
                (body.live_raw ? 'ON' : 'OFF'),
            tag_count: 0,
            tags: [],
        });
    }

    res.json({
        status: 'ok',
        scanner_id: scannerId,
        online: true,
        last_seen: record.last_seen,
        ip: record.ip,
    });
});

app.get('/api/scanner/status', (req, res) => {
    const now = Date.now();
    const scanners = [...scannerHeartbeats.entries()].map(([id, data]) => ({
        scanner_id: id,
        online: now - data.last_seen < SCANNER_ONLINE_THRESHOLD_MS,
        last_seen: data.last_seen,
        last_seen_ago_ms: now - data.last_seen,
        battery: data.battery,
        mode: data.mode,
    }));

    const anyOnline = scanners.some((s) => s.online);
    res.json({
        scanners,
        any_online: anyOnline,
        threshold_ms: SCANNER_ONLINE_THRESHOLD_MS,
    });
});

// 📡 Near-field ingest (onboarding listener — does not mutate inventory)
app.post('/api/scan/near-field-ingest', async (req, res) => {
    const { scanned_tags } = req.body;
    const tagEntries = normalizeScannedTags(scanned_tags);

    if (!tagEntries.length) {
        return res.status(400).json({ error: 'scanned_tags array required (with epc + rssi)' });
    }

    try {
        const settings = await getSystemSettingsCached();
        recordNearFieldReads(tagEntries, settings);
        const nearGate = parseInt(settings.rssi_near_gate, 10) || -55;
        res.json({
            status: 'success',
            recorded: tagEntries.filter((t) => t.rssi != null && t.rssi >= nearGate).length,
            rssi_near_gate: nearGate,
        });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

function checkNearFieldCaptureEligibility(epc, purpose, excludeBinId, callback) {
    const role = purpose === 'boundary' ? 'boundary' : 'item';
    const excludeId = normalizeExcludeId(excludeBinId);

    checkEpcForRole(epc, { role, excludeBinId: excludeId }, (err, result) => {
        if (err) return callback(err);
        if (result.valid) {
            return callback(null, { eligible: true, epc: result.epc || epc });
        }
        callback(null, {
            eligible: false,
            reason: toNearFieldReason(result.reason),
            epc: result.epc || epc,
            existing_name: result.item_name,
            container_id: result.container_id,
            container_name: result.container_name,
        });
    });
}

// 🔍 Validate an EPC against the global registry (items, bin IDs, boundary tags)
app.get('/api/epc/validate', (req, res) => {
    const epc = normalizeEpc(req.query.epc);
    const role = String(req.query.role || 'item').trim().toLowerCase();
    const allowedRoles = new Set(['item', 'boundary', 'container_id']);

    if (!epc) return res.status(400).json({ valid: false, error: 'epc query parameter is required' });
    if (!allowedRoles.has(role)) {
        return res.status(400).json({ valid: false, error: 'role must be item, boundary, or container_id' });
    }

    checkEpcForRole(
        epc,
        {
            role,
            excludeBinId: normalizeExcludeId(req.query.exclude_bin_id),
            excludeItemEpc: normalizeEpc(req.query.exclude_item_epc),
        },
        (err, result) => {
            if (err) return res.status(500).json({ error: err.message });
            if (result.valid) return res.json({ valid: true, epc });
            res.json({
                valid: false,
                reason: result.reason,
                error: result.message,
                epc: result.epc,
                container_id: result.container_id,
                container_name: result.container_name,
                item_name: result.item_name,
            });
        }
    );
});

// 🏷️ Latest ultra-near tag for onboarding / boundary sniffer wizards
app.get('/api/scan/latest-near-field', async (req, res) => {
    const since = parseInt(String(req.query.since || '0'), 10) || 0;
    const purpose = String(req.query.purpose || 'onboarding').trim().toLowerCase();
    const excludeBinId = normalizeExcludeId(req.query.exclude_bin_id);

    try {
        const settings = await getSystemSettingsCached();
        const nearGate = parseInt(settings.rssi_near_gate, 10) || -55;
        const candidate = findLatestUnassignedNearField(since, nearGate);

        if (!candidate) {
            return res.json({
                captured: false,
                rssi_near_gate: nearGate,
                rssi_far_gate: parseInt(settings.rssi_far_gate, 10) || -85,
                purpose,
            });
        }

        checkNearFieldCaptureEligibility(candidate.epc, purpose, excludeBinId, (err, result) => {
            if (err) return res.status(500).json({ error: err.message });

            if (!result.eligible) {
                return res.json({
                    captured: false,
                    reason: result.reason,
                    epc: result.epc,
                    existing_name: result.existing_name,
                    container_id: result.container_id,
                    container_name: result.container_name,
                    rssi_near_gate: nearGate,
                    rssi_far_gate: parseInt(settings.rssi_far_gate, 10) || -85,
                    purpose,
                });
            }

            res.json({
                captured: true,
                epc: candidate.epc,
                rssi: candidate.rssi,
                timestamp: candidate.timestamp,
                rssi_near_gate: nearGate,
                rssi_far_gate: parseInt(settings.rssi_far_gate, 10) || -85,
                purpose,
            });
        });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 📡 Processes bulk scans and updates item locations (hardware, emulator, mobile manual submit)
app.post('/api/scan', async (req, res) => {
    const options = req.body || {};
    const targetContainerEpc = normalizeContainerId(
        options.targetContainerEpc || options.target_container_epc || null
    );
    const scanned_tags = options.scanned_tags || options.tags;

    console.log(`\n--- 📡 Processing Scan: ${scanned_tags ? scanned_tags.length : 0} tags ---`);

    if (!scanned_tags || scanned_tags.length === 0) {
        return res.status(200).json({ status: 'success', message: 'No tags received.' });
    }

    const tagEntries = normalizeScannedTags(scanned_tags);

    try {
        const settings = await getSystemSettingsCached();
        recordNearFieldReads(tagEntries, settings);

        const result = await runInventoryScan(targetContainerEpc, tagEntries, {
            source: 'api-scan',
        });
        res.status(200).json(result);
    } catch (err) {
        console.error(err);
        res.status(500).json({ error: err.message });
    }
});

// 🔫 Nordic ID Merlin keyboard wedge / raw stream gateway
app.post('/api/hardware/merlin-wedge', (req, res, next) => {
    const ct = String(req.get('content-type') || '');
    if (ct.indexOf('application/json') >= 0) return next();
    express.text({ type: '*/*', limit: '2mb' })(req, res, next);
}, async (req, res) => {
        let body = req.body;
        if (typeof body === 'string') {
            try {
                body = JSON.parse(body);
            } catch {
                body = { raw: body };
            }
        }

        const parsed = parseMerlinWedgePayload(body || {});
        console.log(
            `\n--- 🔫 Merlin wedge: ${parsed.tagEntries.length} tag(s) scanner=${parsed.scanner_id} ---`
        );

        if (!parsed.tagEntries.length) {
            return res.status(400).json({
                error:
                    'No EPC tags parsed. Send JSON { scanned_tags, target_container_epc } or plain text (comma/newline separated).',
            });
        }

        try {
            const settings = await getSystemSettings();
            recordNearFieldReads(parsed.tagEntries, settings);
        } catch (err) {
            console.warn('Near-field record failed:', err.message);
        }

        try {
            const result = await runInventoryScan(parsed.targetContainerEpc, parsed.tagEntries, {
                source: 'merlin-wedge',
            });
            res.status(200).json({
                ...result,
                source: 'merlin-wedge',
                scanner_id: parsed.scanner_id,
            });
        } catch (err) {
            console.error(err);
            res.status(500).json({ error: err.message });
        }
});

// 🧪 Remove simulator test tags (TEST-EPC-* items, bins, and related scan logs)
function purgeTestRssiBuffers() {
    const prefix = TEST_EPC_PREFIX.toUpperCase();
    huntRssiByEpc.forEach((_v, key) => {
        if (String(key || '').toUpperCase().indexOf(prefix) === 0) {
            huntRssiByEpc.delete(key);
        }
    });
    for (let i = nearFieldBuffer.length - 1; i >= 0; i--) {
        const epc = String(nearFieldBuffer[i].epc || '').toUpperCase();
        if (epc.indexOf(prefix) === 0) {
            nearFieldBuffer.splice(i, 1);
        }
    }
}

app.delete('/api/test/purge', (req, res) => {
    const likePattern = TEST_EPC_PREFIX + '%';
    const summary = { items: 0, containers: 0, scan_history: 0 };

    db.serialize(() => {
        db.run(
            `DELETE FROM scan_history
             WHERE scanned_epc LIKE ? COLLATE NOCASE
                OR parent_container_epc LIKE ? COLLATE NOCASE`,
            [likePattern, likePattern],
            function (histErr) {
                if (histErr) return res.status(500).json({ error: histErr.message });
                summary.scan_history = this.changes;

                db.run(
                    `DELETE FROM items
                     WHERE epc_id LIKE ? COLLATE NOCASE
                        OR name LIKE ? COLLATE NOCASE
                        OR description LIKE ? COLLATE NOCASE
                        OR upc LIKE ? COLLATE NOCASE`,
                    [likePattern, likePattern, likePattern, likePattern],
                    function (itemsErr) {
                        if (itemsErr) return res.status(500).json({ error: itemsErr.message });
                        summary.items = this.changes;

                        db.run(
                            `DELETE FROM containers
                             WHERE id LIKE ? COLLATE NOCASE
                                OR name LIKE ? COLLATE NOCASE
                                OR description LIKE ? COLLATE NOCASE`,
                            [likePattern, likePattern, likePattern],
                            function (binErr) {
                                if (binErr) return res.status(500).json({ error: binErr.message });
                                summary.containers = this.changes;

                                purgeTestRssiBuffers();

                                activeSearchQueue = activeSearchQueue.filter(
                                    (epc) =>
                                        !String(epc || '')
                                            .toUpperCase()
                                            .startsWith(TEST_EPC_PREFIX)
                                );

                                console.log(
                                    `🗑️ Test purge: ${summary.items} items, ${summary.containers} bins, ${summary.scan_history} log rows`
                                );

                                res.json({
                                    status: 'success',
                                    prefix: TEST_EPC_PREFIX,
                                    deleted: summary,
                                    message: `Purged all rows matching prefix ${TEST_EPC_PREFIX}`,
                                });
                            }
                        );
                    }
                );
            }
        );
    });
});

// 🔍 Universal product enrichment (UPC barcode or free-text search)
app.post('/api/lookup/product', async (req, res) => {
    const body = req.body || {};
    const upcRaw = body.upc != null ? String(body.upc).trim() : '';
    const textRaw = body.text != null ? String(body.text).trim() : '';

    if (!upcRaw && !textRaw) {
        return res.status(400).json({
            success: false,
            error: 'Provide { upc: string } or { text: string }',
        });
    }
    if (upcRaw && textRaw) {
        return res.status(400).json({
            success: false,
            error: 'Provide either upc or text, not both',
        });
    }

    try {
        if (upcRaw) {
            const upc = normalizeUpc(upcRaw);
            if (!upc) {
                return res.status(400).json({
                    success: false,
                    error: 'Invalid UPC — use 8–14 digits',
                });
            }

            const cached = await getCachedUpc(upc);
            if (cached) {
                const enriched = {
                    success: true,
                    multiple: false,
                    title: cached.name,
                    description: cached.description,
                    category: cached.category,
                    image_url: cached.image_url,
                    upc: cached.upc,
                    brand: cached.brand,
                    source: cached.source,
                    providers_tried: ['local_cache'],
                    cached: true,
                };
                return res.json(enriched);
            }

            const settings = await getSystemSettings();
            const key = settings.upcitemdb_api_key || process.env.UPCITEMDB_API_KEY || '';
            const result = await lookupProduct({ upc }, { upcitemdbKey: key });
            if (result.success) {
                await saveUpcCache({
                    found: true,
                    upc: result.upc,
                    source: result.source,
                    name: result.title,
                    brand: result.brand,
                    category: result.category,
                    description: result.description,
                    image_url: result.image_url,
                });
            }
            return res.json(result);
        }

        const settings = await getSystemSettings();
        const key = settings.upcitemdb_api_key || process.env.UPCITEMDB_API_KEY || '';
        const result = await lookupProduct({ text: textRaw }, { upcitemdbKey: key });
        res.json(result);
    } catch (err) {
        res.status(500).json({ success: false, error: err.message });
    }
});

// 🏷️ Hybrid UPC lookup (local cache → Open*Facts chain → optional UPCitemdb)
app.get('/api/upc/lookup/:code', async (req, res) => {
    const upc = normalizeUpc(req.params.code);
    if (!upc) {
        return res.status(400).json({ found: false, error: 'Invalid UPC — use 8–14 digits' });
    }

    try {
        const cached = await getCachedUpc(upc);
        if (cached) {
            return res.json({
                ...cacheRowToLookup(cached),
                providers_tried: ['local_cache'],
            });
        }

        const settings = await getSystemSettingsCached();
        const key = settings.upcitemdb_api_key || process.env.UPCITEMDB_API_KEY || '';
        const result = await lookupUpcHybrid(upc, { upcitemdbKey: key });

        if (result.found) {
            await saveUpcCache(result);
        }

        res.json(result);
    } catch (err) {
        res.status(500).json({ found: false, error: err.message });
    }
});

// ➕ Register item with RFID EPC (+ optional UPC metadata)
app.post('/api/items', async (req, res) => {
    const {
        epc_id,
        name,
        description,
        category,
        upc,
        image_url,
        container_id,
        home_container_id,
    } = req.body;

    const epc = normalizeEpc(epc_id);
    const itemName = name == null ? '' : String(name).trim();
    const normalizedUpc = upc ? normalizeUpc(upc) : null;

    if (!epc) return res.status(400).json({ error: 'epc_id is required' });
    if (!itemName) return res.status(400).json({ error: 'name is required' });

    try {
        const registry = await checkEpcForRoleAsync(epc, { role: 'item' });
        if (!registry.valid) {
            return res.status(409).json({
                error: registry.message,
                reason: registry.reason,
            });
        }
    } catch (err) {
        return res.status(500).json({ error: err.message });
    }

    const normalizedImageUrl =
        image_url == null || String(image_url).trim() === ''
            ? null
            : String(image_url).trim();
    const sanitizedCategory =
        category != null && String(category).trim() !== ''
            ? sanitizeCategoryString(category)
            : null;

    db.run(
        `INSERT INTO items (epc_id, name, description, category, upc, image_url, container_id, home_container_id)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
        [
            epc,
            itemName,
            description ?? null,
            sanitizedCategory,
            normalizedUpc,
            normalizedImageUrl,
            normalizeContainerId(container_id),
            normalizeContainerId(home_container_id),
        ],
        function (err) {
            if (err) {
                if (String(err.message).includes('UNIQUE')) {
                    return res.status(409).json({ error: 'Item with this EPC already exists' });
                }
                return res.status(500).json({ error: err.message });
            }
            invalidateKnownEpcRegistryCache();
            recentHardwareReads.delete(normalizeScanTag(epc));
            res.status(201).json({
                status: 'success',
                epc_id: epc,
                name: itemName,
                description,
                category: sanitizedCategory,
                upc: normalizedUpc,
                image_url: normalizedImageUrl,
                container_id: normalizeContainerId(container_id),
                home_container_id: normalizeContainerId(home_container_id),
            });
        }
    );
});

// 🔄 Replace a damaged RFID tag (new EPC must pass global registry)
app.post('/api/items/:epc_id/replace-epc', async (req, res) => {
    const oldEpc = normalizeEpc(req.params.epc_id);
    const newEpc = normalizeEpc(req.body?.new_epc_id);

    if (!oldEpc) return res.status(400).json({ error: 'epc_id is required' });
    if (!newEpc) return res.status(400).json({ error: 'new_epc_id is required' });
    if (epcEquals(oldEpc, newEpc)) {
        return res.status(400).json({ error: 'New EPC must be different from the current tag' });
    }

    try {
        const registry = await checkEpcForRoleAsync(newEpc, { role: 'item' });
        if (!registry.valid) {
            return res.status(409).json({
                error: registry.message,
                reason: registry.reason,
            });
        }
    } catch (err) {
        return res.status(500).json({ error: err.message });
    }

    db.get(`SELECT epc_id, name FROM items WHERE LOWER(epc_id) = LOWER(?)`, [oldEpc], (findErr, row) => {
        if (findErr) return res.status(500).json({ error: findErr.message });
        if (!row) return res.status(404).json({ error: 'Item not found' });

        db.serialize(() => {
            db.run(`UPDATE items SET epc_id = ? WHERE LOWER(epc_id) = LOWER(?)`, [newEpc, oldEpc], function (updErr) {
                if (updErr) {
                    if (String(updErr.message).includes('UNIQUE')) {
                        return res.status(409).json({ error: 'Item with this EPC already exists' });
                    }
                    return res.status(500).json({ error: updErr.message });
                }
                if (this.changes === 0) {
                    return res.status(404).json({ error: 'Item not found' });
                }

                db.run(
                    `UPDATE scan_history SET scanned_epc = ? WHERE LOWER(scanned_epc) = LOWER(?)`,
                    [newEpc, oldEpc],
                    (histErr) => {
                        if (histErr) console.error('scan_history EPC remap:', histErr.message);

                        activeSearchQueue = activeSearchQueue.map((id) =>
                            epcEquals(id, oldEpc) ? newEpc : id
                        );

                        res.json({
                            status: 'success',
                            old_epc_id: row.epc_id,
                            epc_id: newEpc,
                            name: row.name,
                        });
                    }
                );
            });
        });
    });
});

// 🗑️ Delete an inventory item
app.delete('/api/items/:epc_id', (req, res) => {
    const epc = normalizeEpc(req.params.epc_id);
    if (!epc) return res.status(400).json({ error: 'epc_id is required' });

    db.run(`DELETE FROM items WHERE LOWER(epc_id) = LOWER(?)`, [epc], function (err) {
        if (err) return res.status(500).json({ error: err.message });
        if (this.changes === 0) return res.status(404).json({ error: 'Item not found' });

        activeSearchQueue = activeSearchQueue.filter((id) => !epcEquals(id, epc));
        res.json({ status: 'success', deleted_epc_id: epc });
    });
});

// ✏️ Update item metadata (name, description, category, bins, current location)
app.put('/api/items/:epc_id', (req, res) => {
    const { epc_id } = req.params;
    const { name, description, category, home_container_id, container_id, upc, image_url } =
        req.body;
    const normalizedHomeContainerId = normalizeContainerId(home_container_id);
    const normalizedContainerId = container_id !== undefined
        ? normalizeContainerId(container_id)
        : undefined;
    const normalizedUpc = upc === undefined ? undefined : (upc ? normalizeUpc(upc) : null);

    if (!name || typeof name !== 'string' || !name.trim()) {
        return res.status(400).json({ error: 'name is required' });
    }

    const sanitizedCategory =
        category != null && String(category).trim() !== ''
            ? sanitizeCategoryString(category)
            : null;

    const sets = ['name = ?', 'description = ?', 'category = ?', 'home_container_id = ?'];
    const params = [
        name.trim(),
        description ?? null,
        sanitizedCategory,
        normalizedHomeContainerId,
    ];

    if (normalizedContainerId !== undefined) {
        sets.push('container_id = ?');
        params.push(normalizedContainerId);
    }
    if (normalizedUpc !== undefined) {
        sets.push('upc = ?');
        params.push(normalizedUpc);
    }
    if (image_url !== undefined) {
        sets.push('image_url = ?');
        params.push(
            image_url == null || String(image_url).trim() === ''
                ? null
                : String(image_url).trim()
        );
    }
    params.push(epc_id);

    db.run(
        `UPDATE items SET ${sets.join(', ')} WHERE epc_id = ?`,
        params,
        function (err) {
            if (err) return res.status(500).json({ error: err.message });
            if (this.changes === 0) return res.status(404).json({ error: 'Item not found' });
            res.json({
                status: 'success',
                epc_id,
                name: name.trim(),
                description,
                category: sanitizedCategory,
                home_container_id: normalizedHomeContainerId,
                container_id: normalizedContainerId,
                upc: normalizedUpc !== undefined ? normalizedUpc : undefined,
            });
        }
    );
});

// 📦 List all registered bins
app.get('/api/containers', (req, res) => {
    db.all(
        `SELECT id, name, description, boundary_tag_a, boundary_tag_b FROM containers ORDER BY name ASC`,
        [],
        (err, rows) => {
            if (err) return res.status(500).json({ error: err.message });
            res.json(rows);
        }
    );
});

function generateContainerId(name) {
    const slug = String(name ?? '')
        .trim()
        .toUpperCase()
        .replace(/[^A-Z0-9]+/g, '-')
        .replace(/^-+|-+$/g, '')
        .slice(0, 22);
    const base = slug || 'BIN';
    const suffix = Date.now().toString(36).toUpperCase().slice(-5);
    return base + '-' + suffix;
}

// 📦 Create a new bin
app.post('/api/containers', async (req, res) => {
    const { id, name, description, boundary_tag_a, boundary_tag_b } = req.body;
    const binName = name == null ? '' : String(name).trim();
    let binId = normalizeEpc(id);
    const boundaryA = normalizeBoundaryTag(boundary_tag_a);
    const boundaryB = normalizeBoundaryTag(boundary_tag_b);

    if (!binName) return res.status(400).json({ error: 'name is required' });
    if (!binId) binId = generateContainerId(binName);

    try {
        const validation = await validateContainerSaveAsync({
            binId,
            boundaryA,
            boundaryB,
            excludeBinId: null,
        });
        if (!validation.ok) {
            return res.status(400).json({
                error: validation.error,
                reason: validation.reason,
            });
        }
    } catch (err) {
        return res.status(500).json({ error: err.message });
    }

    db.run(
        `INSERT INTO containers (id, name, description, boundary_tag_a, boundary_tag_b) VALUES (?, ?, ?, ?, ?)`,
        [binId, binName, description ? String(description).trim() : null, boundaryA, boundaryB],
        function (err) {
            if (err) {
                if (String(err.message).includes('UNIQUE')) {
                    return res.status(409).json({ error: 'A bin with this ID already exists' });
                }
                return res.status(500).json({ error: err.message });
            }
            invalidateKnownEpcRegistryCache();
            res.status(201).json({
                id: binId,
                name: binName,
                description: description ? String(description).trim() : null,
                boundary_tag_a: boundaryA,
                boundary_tag_b: boundaryB,
            });
        }
    );
});

// 📦 Update an existing bin by ID (upsert for scan-time registration)
app.put('/api/containers/:id', async (req, res) => {
    const { id: paramId } = req.params;
    const { name, description, boundary_tag_a, boundary_tag_b } = req.body;

    if (!name || typeof name !== 'string' || !name.trim()) {
        return res.status(400).json({ error: 'name is required' });
    }

    const desc = description == null || String(description).trim() === ''
        ? null
        : String(description).trim();
    const boundaryA = normalizeBoundaryTag(boundary_tag_a);
    const boundaryB = normalizeBoundaryTag(boundary_tag_b);

    try {
        const validation = await validateContainerSaveAsync({
            binId: paramId,
            boundaryA,
            boundaryB,
            excludeBinId: paramId,
        });
        if (!validation.ok) {
            return res.status(400).json({
                error: validation.error,
                reason: validation.reason,
            });
        }
    } catch (err) {
        return res.status(500).json({ error: err.message });
    }

    db.get(`SELECT id FROM containers WHERE id = ?`, [paramId], (err, row) => {
        if (err) return res.status(500).json({ error: err.message });

        if (row) {
            db.run(
                `UPDATE containers
                 SET name = ?, description = ?, boundary_tag_a = ?, boundary_tag_b = ?
                 WHERE id = ?`,
                [name.trim(), desc, boundaryA, boundaryB, paramId],
                function (updateErr) {
                    if (updateErr) return res.status(500).json({ error: updateErr.message });
                    res.json({
                        id: paramId,
                        name: name.trim(),
                        description: desc,
                        boundary_tag_a: boundaryA,
                        boundary_tag_b: boundaryB,
                    });
                }
            );
        } else {
            db.run(
                `INSERT INTO containers (id, name, description, boundary_tag_a, boundary_tag_b)
                 VALUES (?, ?, ?, ?, ?)`,
                [paramId, name.trim(), desc, boundaryA, boundaryB],
                function (insertErr) {
                    if (insertErr) return res.status(500).json({ error: insertErr.message });
                    res.status(201).json({
                        id: paramId,
                        name: name.trim(),
                        description: desc,
                        boundary_tag_a: boundaryA,
                        boundary_tag_b: boundaryB,
                    });
                }
            );
        }
    });
});

// 🗑️ Delete a bin (clears item references, then removes container row)
app.delete('/api/containers/:id', (req, res) => {
    const binId = req.params.id;

    db.get(`SELECT id, name FROM containers WHERE id = ?`, [binId], (err, row) => {
        if (err) return res.status(500).json({ error: err.message });
        if (!row) return res.status(404).json({ error: 'Bin not found' });

        db.serialize(() => {
            db.run(`UPDATE items SET container_id = NULL WHERE container_id = ?`, [binId]);
            db.run(`UPDATE items SET home_container_id = NULL WHERE home_container_id = ?`, [binId], function (clearErr) {
                if (clearErr) return res.status(500).json({ error: clearErr.message });

                const clearedLinks = this.changes;

                db.run(`DELETE FROM containers WHERE id = ?`, [binId], function (delErr) {
                    if (delErr) return res.status(500).json({ error: delErr.message });
                    if (this.changes === 0) {
                        return res.status(404).json({ error: 'Bin not found' });
                    }
                    res.json({
                        status: 'success',
                        id: binId,
                        name: row.name,
                        cleared_item_links: clearedLinks,
                    });
                });
            });
        });
    });
});

// 🔍 Find Mode: queue + multi-target hunt RSSI (WebSocket / SSE / long-poll / fast-poll)
app.get('/api/search/target', async (req, res) => {
    try {
        const clientRev = parseInt(String(req.query.rev ?? ''), 10);
        const wait = req.query.wait === '1' || req.query.long === '1';
        const compact = req.query.compact === '1';
        const timeoutMs = Math.min(
            Math.max(parseInt(String(req.query.timeout ?? ''), 10) || 25000, 500),
            30000
        );

        if (wait && !Number.isNaN(clientRev) && clientRev === huntRevision) {
            return registerLongPollWaiter(req, res, timeoutMs);
        }

        const payload = await buildSearchTargetPayload();

        if (compact && !Number.isNaN(clientRev) && clientRev === payload.revision) {
            return res.json({ unchanged: true, revision: huntRevision });
        }

        res.json(payload);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

app.get('/api/hunt/stream', async (req, res) => {
    res.setHeader('Content-Type', 'text/event-stream; charset=utf-8');
    res.setHeader('Cache-Control', 'no-cache, no-transform');
    res.setHeader('Connection', 'keep-alive');
    res.flushHeaders?.();

    const client = { res };
    huntSseClients.add(client);

    try {
        const payload = await buildSearchTargetPayload();
        res.write(`data: ${JSON.stringify(payload)}\n\n`);
    } catch (err) {
        huntSseClients.delete(client);
        return res.status(500).end();
    }

    req.on('close', () => {
        huntSseClients.delete(client);
    });
});

app.post('/api/search/target', (req, res) => {
    const { epc_ids } = req.body;

    if (!epc_ids || !Array.isArray(epc_ids) || epc_ids.length === 0) {
        activeSearchQueue = [];
        huntRssiByEpc.clear();
    } else {
        activeSearchQueue = [...new Set(
            epc_ids
                .map((id) => (id == null ? '' : String(id).trim()))
                .filter(Boolean)
        )];
        const allowed = new Set(activeSearchQueue.map((id) => normalizeScanTag(id)));
        huntRssiByEpc.forEach((_v, key) => {
            if (!allowed.has(key)) huntRssiByEpc.delete(key);
        });
    }

    if (activeSearchQueue.length === 0) {
        console.log('🔍 [Multi Hunt] Queue cleared.');
    } else {
        console.log(`🔍 [Multi Hunt] Queue updated (${activeSearchQueue.length} target(s)):`);
        activeSearchQueue.forEach((epc, i) => console.log(`   ${i + 1}. ${epc}`));
    }

    bumpHuntRevision();

    buildSearchTargetPayload()
        .then((payload) => res.json(payload))
        .catch((err) => res.status(500).json({ error: err.message }));
});

// 📱 Compact sync payload for native Windows CE handheld (cache + offline Find)
app.get('/api/handheld/sync', async (req, res) => {
    try {
        const [items, containers, settings] = await Promise.all([
            dbAll(
                `SELECT items.epc_id, items.name, items.category, items.upc,
                        items.container_id, items.home_container_id,
                        containers.name AS container_name,
                        home_containers.name AS home_container_name
                 FROM items
                 LEFT JOIN containers ON items.container_id = containers.id
                 LEFT JOIN containers AS home_containers ON items.home_container_id = home_containers.id
                 ORDER BY items.name ASC`
            ),
            dbAll(
                `SELECT id, name, boundary_tag_a, boundary_tag_b FROM containers ORDER BY name ASC`
            ),
            getSystemSettingsCached(),
        ]);

        sendJsonOrJsonp(req, res, {
            synced_at: Date.now(),
            items: items.map((item) => ({
                ...item,
                status: computeItemStatus(item),
            })),
            containers,
            activeSearchQueue: [...activeSearchQueue],
            rssi_near_gate: parseInt(settings.rssi_near_gate, 10) || -55,
            rssi_far_gate: parseInt(settings.rssi_far_gate, 10) || -85,
        });
    } catch (err) {
        sendJsonOrJsonp(req, res, { error: err.message }, 500);
    }
});

// 🌐 API to get all current inventory and history for the frontend dashboard
app.get('/api/dashboard', async (req, res) => {
    try {
        const [items, containers, history] = await Promise.all([
            dbAll(
                `SELECT items.*,
                        containers.name AS container_name,
                        home_containers.name AS home_container_name
                 FROM items
                 LEFT JOIN containers ON items.container_id = containers.id
                 LEFT JOIN containers AS home_containers ON items.home_container_id = home_containers.id`
            ),
            dbAll(`SELECT id, name, description FROM containers ORDER BY name ASC`),
            dbAll(`SELECT * FROM scan_history ORDER BY timestamp DESC LIMIT 10`),
        ]);

        res.json({
            items: items.map((item) => ({
                ...item,
                status: computeItemStatus(item),
            })),
            containers,
            history,
        });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

function dbAllAsync(sql, params = []) {
    return new Promise((resolve, reject) => {
        db.all(sql, params, (err, rows) => {
            if (err) reject(err);
            else resolve(rows || []);
        });
    });
}

function dbRunAsync(sql, params = []) {
    return new Promise((resolve, reject) => {
        db.run(sql, params, function (err) {
            if (err) reject(err);
            else resolve({ changes: this.changes });
        });
    });
}

// 🏷️ Master category taxonomy (semicolon tags on items)
app.get('/api/categories', async (req, res) => {
    try {
        const rows = await dbAllAsync(
            `SELECT category FROM items WHERE category IS NOT NULL AND TRIM(category) != ''`
        );
        res.json({ categories: collectUniqueCategories(rows) });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

app.post('/api/categories/operations', async (req, res) => {
    const action = String(req.body?.action ?? '').trim().toLowerCase();
    const fromTag = String(req.body?.from ?? '').trim();
    const toTag = String(req.body?.to ?? req.body?.merge_into ?? '').trim();

    if (!['rename', 'delete', 'merge'].includes(action)) {
        return res.status(400).json({ error: 'action must be rename, delete, or merge' });
    }
    if (!fromTag) {
        return res.status(400).json({ error: 'from is required (category tag to change)' });
    }
    if ((action === 'rename' || action === 'merge') && !toTag) {
        return res.status(400).json({ error: 'to is required for rename and merge' });
    }
    if ((action === 'rename' || action === 'merge') && fromTag === toTag) {
        return res.status(400).json({ error: 'from and to must be different' });
    }

    try {
        const rows = await dbAllAsync(
            `SELECT epc_id, category FROM items WHERE category IS NOT NULL AND TRIM(category) != ''`
        );

        const transformFn =
            action === 'rename'
                ? (tags) => renameTagInList(tags, fromTag, toTag)
                : action === 'delete'
                  ? (tags) => deleteTagFromList(tags, fromTag)
                  : (tags) => mergeTagInList(tags, fromTag, toTag);

        let updatedItems = 0;
        for (const row of rows) {
            if (!splitCategoryTags(row.category).includes(fromTag)) continue;
            const nextCategory = transformCategoryField(row.category, transformFn);
            const prev = row.category == null ? null : String(row.category);
            const next = nextCategory == null ? null : String(nextCategory);
            if (prev === next) continue;
            await dbRunAsync(`UPDATE items SET category = ? WHERE epc_id = ?`, [
                nextCategory,
                row.epc_id,
            ]);
            updatedItems += 1;
        }

        const refreshed = await dbAllAsync(
            `SELECT category FROM items WHERE category IS NOT NULL AND TRIM(category) != ''`
        );

        res.json({
            status: 'success',
            action,
            from: fromTag,
            to: action === 'delete' ? null : toTag,
            updated_items: updatedItems,
            categories: collectUniqueCategories(refreshed),
        });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// ⚙️ Admin: system settings
app.get('/api/admin/settings', (req, res) => {
    getSystemSettings()
        .then((settings) => res.json(settings))
        .catch((err) => res.status(500).json({ error: err.message }));
});

app.post('/api/admin/settings', (req, res) => {
    const sanitized = sanitizeSettingsInput(req.body);
    const keys = Object.keys(sanitized).filter((k) => ALLOWED_SETTING_KEYS.has(k));

    if (keys.length === 0) {
        return res.status(400).json({ error: 'No valid settings provided' });
    }

    const stmt = db.prepare(
        `INSERT OR REPLACE INTO system_settings (key, value) VALUES (?, ?)`
    );

    db.serialize(() => {
        keys.forEach((key) => stmt.run(key, sanitized[key]));
        stmt.finalize((finalizeErr) => {
            if (finalizeErr) return res.status(500).json({ error: finalizeErr.message });
            invalidateSettingsCache();
            getSystemSettings()
                .then((settings) => {
                    settingsCache = { value: settings, at: Date.now() };
                    res.json({ status: 'success', settings });
                })
                .catch((e) => res.status(500).json({ error: e.message }));
        });
    });
});

app.post('/api/admin/purge-unknown', (req, res) => {
    db.run(
        `DELETE FROM items WHERE name LIKE 'Unknown RFID Tag%'`,
        [],
        function (err) {
            if (err) return res.status(500).json({ error: err.message });
            res.json({
                status: 'success',
                deleted_count: this.changes,
                message: `Removed ${this.changes} unassigned ghost tag(s).`,
            });
        }
    );
});

app.post('/api/admin/clear-history', (req, res) => {
    db.run(`DELETE FROM scan_history`, [], function (err) {
        if (err) return res.status(500).json({ error: err.message });
        res.json({
            status: 'success',
            deleted_count: this.changes,
            message: `Purged ${this.changes} scan history record(s).`,
        });
    });
});

const server = http.createServer(app);

const huntWss = new WebSocket.Server({ server, path: '/api/hunt/ws' });
huntWss.on('connection', (ws) => {
    huntWsClients.add(ws);
    buildSearchTargetPayload()
        .then((payload) => {
            if (ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify(payload));
            }
        })
        .catch(() => {});

    ws.on('close', () => huntWsClients.delete(ws));
    ws.on('error', () => huntWsClients.delete(ws));
});

server.listen(PORT, '0.0.0.0', () => {
    const deployHealth = deployFilesStatus();
    console.log(`🚀 RFID Backend Server with DB active on http://0.0.0.0:${PORT}`);
    console.log(`📡 Hunt push: WebSocket ws://0.0.0.0:${PORT}/api/hunt/ws | SSE /api/hunt/stream | long-poll GET /api/search/target?wait=1&rev=N`);
    console.log(`📂 Public root: ${PUBLIC_DIR} (cwd=${process.cwd()})`);
    console.log(
        `📡 Deploy scanner-live: ${deployHealth.pages['scanner-live.html'].exists ? 'OK' : 'MISSING — copy public/deploy/scanner-live.html'}`
    );
});