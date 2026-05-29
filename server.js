const express = require('express');
const db = require('./database'); // Import our database setup
const { lookupUpcHybrid, normalizeUpc } = require('./upc-lookup');
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
const app = express();
const PORT = process.env.PORT || 3000;

let activeSearchQueue = [];

/** Recent ultra-near reads for onboarding wizard (newest first). */
const nearFieldBuffer = [];
const NEAR_FIELD_BUFFER_MAX = 120;

/** Handheld scanner heartbeats (scanner_id → { lastSeen, ...meta }). */
const scannerHeartbeats = new Map();
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
// Serves index.html, mobile.html, emulator.html, and /public assets — no route conflict with /api/*
app.use(express.static('public'));

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

function recordNearFieldReads(tagEntries, settings) {
    const nearGate = parseInt(settings.rssi_near_gate, 10) || -55;
    const now = Date.now();
    let recorded = 0;

    tagEntries.forEach(({ epc, rssi }) => {
        if (!epc || rssi == null || Number.isNaN(rssi)) return;
        if (rssi >= nearGate) {
            nearFieldBuffer.unshift({ epc, rssi, timestamp: now });
            recorded += 1;
        }
    });

    while (nearFieldBuffer.length > NEAR_FIELD_BUFFER_MAX) {
        nearFieldBuffer.pop();
    }

    if (recorded > 0) {
        console.log(`📡 Near-field buffer: +${recorded} ultra-near read(s) (gate ≥ ${nearGate} dBm)`);
    }
}

function findLatestUnassignedNearField(sinceMs, nearGate) {
    const candidates = nearFieldBuffer.filter(
        (read) => read.timestamp > sinceMs && read.rssi >= nearGate
    );
    if (!candidates.length) return null;

    candidates.sort((a, b) => b.rssi - a.rssi || b.timestamp - a.timestamp);
    return candidates[0];
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

function processScannedTags(scannedTags, targetContainerEpc) {
    const logStmt = db.prepare(
        `INSERT INTO scan_history (scanned_epc, parent_container_epc, action) VALUES (?, ?, ?)`
    );
    const updateItemStmt = db.prepare(`UPDATE items SET container_id = ? WHERE epc_id = ?`);

    db.serialize(() => {
        scannedTags.forEach((epc) => {
            db.get(
                `SELECT name, container_id, home_container_id FROM items WHERE epc_id = ?`,
                [epc],
                (err, item) => {
                    if (err) console.error(err);

                    if (item) {
                        if (item.container_id !== targetContainerEpc) {
                            console.log(
                                `📦 MOVED: '${item.name}' [${epc}] moved to bin [${targetContainerEpc}]`
                            );
                            updateItemStmt.run(targetContainerEpc, epc);
                            logStmt.run(epc, targetContainerEpc, 'MOVED');
                            maybeNotifyMisplaced(item, epc, targetContainerEpc);
                        } else {
                            console.log(
                                `🎯 CONFIRMED: '${item.name}' is still in bin [${targetContainerEpc}]`
                            );
                            logStmt.run(epc, targetContainerEpc, 'FOUND');
                            maybeNotifyMisplaced(item, epc, targetContainerEpc);
                        }
                    } else {
                        checkEpcForRole(epc, { role: 'item' }, (regErr, regResult) => {
                            if (regErr) {
                                console.error(regErr);
                                return;
                            }
                            if (!regResult.valid) {
                                console.log(
                                    `⚠️ Skipped auto-register [${epc}]: ${regResult.message}`
                                );
                                logStmt.run(epc, targetContainerEpc, 'REJECTED');
                                return;
                            }
                            console.log(`🆕 UNKNOWN TAG DETECTED: [${epc}]. Creating placeholder entry.`);
                            db.run(
                                `INSERT INTO items (epc_id, name, container_id) VALUES (?, ?, ?)`,
                                [epc, `Unknown RFID Tag (${epc.slice(-4)})`, targetContainerEpc]
                            );
                            logStmt.run(epc, targetContainerEpc, 'REGISTERED');
                        });
                    }
                }
            );
        });
    });
}

// 📡 Scanner connectivity heartbeat (Merlin handheld / hardware client)
app.post('/api/scanner/heartbeat', (req, res) => {
    const scannerId =
        req.body.scanner_id == null ? '' : String(req.body.scanner_id).trim();
    if (!scannerId) {
        return res.status(400).json({ error: 'scanner_id is required' });
    }

    const record = {
        scanner_id: scannerId,
        last_seen: Date.now(),
        ip: req.ip,
        user_agent: req.get('user-agent') || null,
        battery: req.body.battery ?? null,
        mode: req.body.mode ?? null,
    };

    scannerHeartbeats.set(scannerId, record);
    res.json({ status: 'ok', scanner_id: scannerId, online: true });
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
        const settings = await getSystemSettings();
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
        const settings = await getSystemSettings();
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

// 📡 The Endpoint: Processes bulk scans and updates item locations
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
    const normalizedTags = tagEntries.map((t) => t.epc);

    try {
        const settings = await getSystemSettings();
        recordNearFieldReads(tagEntries, settings);
    } catch (err) {
        console.warn('Near-field record failed:', err.message);
    }

    db.all(
        `SELECT id, name, boundary_tag_a, boundary_tag_b FROM containers
         WHERE TRIM(COALESCE(boundary_tag_a, '')) != ''
           AND TRIM(COALESCE(boundary_tag_b, '')) != ''`,
        [],
        (err, boundaryContainers) => {
            if (err) {
                console.error(err);
                return res.status(500).json({ error: err.message });
            }

            let effectiveTarget = targetContainerEpc;
            let tagsToProcess = normalizedTags;

            const zoneMatch = findSpatialZoneMatch(normalizedTags, boundaryContainers);
            if (zoneMatch) {
                const { container, tagA, tagB } = zoneMatch;
                effectiveTarget = container.id;
                const boundarySet = new Set([tagA, tagB]);
                tagsToProcess = normalizedTags.filter((epc) => !boundarySet.has(epc));

                console.log(
                    `🎯 Spatial Zone Match: Isolated ${tagsToProcess.length} items between boundaries of bin [${container.name}]`
                );
            }

            processScannedTags(tagsToProcess, effectiveTarget);

            res.status(200).json({
                status: 'success',
                message: `Database processed ${tagsToProcess.length} tags.`,
                spatial_zone: zoneMatch
                    ? { container_id: zoneMatch.container.id, container_name: zoneMatch.container.name }
                    : null,
            });
        }
    );
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

        const settings = await getSystemSettings();
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

    db.run(
        `INSERT INTO items (epc_id, name, description, category, upc, container_id, home_container_id)
         VALUES (?, ?, ?, ?, ?, ?, ?)`,
        [
            epc,
            itemName,
            description ?? null,
            category ?? null,
            normalizedUpc,
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
            res.status(201).json({
                status: 'success',
                epc_id: epc,
                name: itemName,
                description,
                category,
                upc: normalizedUpc,
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
    const { name, description, category, home_container_id, container_id, upc } = req.body;
    const normalizedHomeContainerId = normalizeContainerId(home_container_id);
    const normalizedContainerId = container_id !== undefined
        ? normalizeContainerId(container_id)
        : undefined;
    const normalizedUpc = upc === undefined ? undefined : (upc ? normalizeUpc(upc) : null);

    if (!name || typeof name !== 'string' || !name.trim()) {
        return res.status(400).json({ error: 'name is required' });
    }

    const sets = ['name = ?', 'description = ?', 'category = ?', 'home_container_id = ?'];
    const params = [
        name.trim(),
        description ?? null,
        category ?? null,
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
                category,
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

// 📦 Create a new bin
app.post('/api/containers', async (req, res) => {
    const { id, name, description, boundary_tag_a, boundary_tag_b } = req.body;
    const binId = normalizeEpc(id);
    const binName = name == null ? '' : String(name).trim();
    const boundaryA = normalizeBoundaryTag(boundary_tag_a);
    const boundaryB = normalizeBoundaryTag(boundary_tag_b);

    if (!binId) return res.status(400).json({ error: 'id is required' });
    if (!binName) return res.status(400).json({ error: 'name is required' });

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

// 🔍 Find Mode: Nordic scanner polls GET; dashboard sets batch queue via POST
app.get('/api/search/target', (req, res) => {
    res.json({ activeSearchQueue });
});

app.post('/api/search/target', (req, res) => {
    const { epc_ids } = req.body;

    if (!epc_ids || !Array.isArray(epc_ids) || epc_ids.length === 0) {
        activeSearchQueue = [];
    } else {
        activeSearchQueue = [...new Set(
            epc_ids
                .map((id) => (id == null ? '' : String(id).trim()))
                .filter(Boolean)
        )];
    }

    if (activeSearchQueue.length === 0) {
        console.log('🔍 [Batch Hunt] Queue cleared.');
    } else {
        console.log(`🔍 [Batch Hunt] Queue updated (${activeSearchQueue.length} target(s)):`);
        activeSearchQueue.forEach((epc, i) => console.log(`   ${i + 1}. ${epc}`));
    }

    res.json({ activeSearchQueue });
});

// 🌐 API to get all current inventory and history for the frontend dashboard
app.get('/api/dashboard', (req, res) => {
    const data = {};
    
    // Get all items and their current bin assignments
    db.all(`SELECT items.*,
                   containers.name AS container_name,
                   home_containers.name AS home_container_name
            FROM items 
            LEFT JOIN containers ON items.container_id = containers.id
            LEFT JOIN containers AS home_containers ON items.home_container_id = home_containers.id`, [], (err, items) => {
        if (err) return res.status(500).json({ error: err.message });
        data.items = items.map((item) => ({
            ...item,
            status: computeItemStatus(item)
        }));

        db.all(`SELECT id, name, description FROM containers ORDER BY name ASC`, [], (err, containers) => {
            if (err) return res.status(500).json({ error: err.message });
            data.containers = containers;

            // Get the latest 10 scans for the live history feed
            db.all(`SELECT * FROM scan_history ORDER BY timestamp DESC LIMIT 10`, [], (err, history) => {
                if (err) return res.status(500).json({ error: err.message });
                data.history = history;

                res.json(data);
            });
        });
    });
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
            getSystemSettings()
                .then((settings) => res.json({ status: 'success', settings }))
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

app.listen(PORT, () => {
    console.log(`🚀 RFID Backend Server with DB active on port ${PORT}`);
});