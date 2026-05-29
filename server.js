const express = require('express');
const db = require('./database'); // Import our database setup
const { lookupUpcHybrid, normalizeUpc } = require('./upc-lookup');
const app = express();
const PORT = process.env.PORT || 3000;

let activeSearchQueue = [];

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
// Serves index.html, mobile.html, and assets — no route conflict with /api/*
app.use(express.static('public'));

function normalizeScanTag(epc) {
    return epc == null ? '' : String(epc).trim();
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
                        console.log(`🆕 UNKNOWN TAG DETECTED: [${epc}]. Creating placeholder entry.`);
                        db.run(
                            `INSERT INTO items (epc_id, name, container_id) VALUES (?, ?, ?)`,
                            [epc, `Unknown RFID Tag (${epc.slice(-4)})`, targetContainerEpc]
                        );
                        logStmt.run(epc, targetContainerEpc, 'REGISTERED');
                    }
                }
            );
        });
    });
}

// 📡 The Endpoint: Processes bulk scans and updates item locations
app.post('/api/scan', (req, res) => {
    const { target_container_epc, scanned_tags } = req.body;

    console.log(`\n--- 📡 Processing Scan: ${scanned_tags ? scanned_tags.length : 0} tags ---`);

    if (!scanned_tags || scanned_tags.length === 0) {
        return res.status(200).json({ status: 'success', message: 'No tags received.' });
    }

    const normalizedTags = scanned_tags.map(normalizeScanTag).filter(Boolean);

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

            let effectiveTarget = target_container_epc;
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
app.post('/api/items', (req, res) => {
    const {
        epc_id,
        name,
        description,
        category,
        upc,
        container_id,
        home_container_id,
    } = req.body;

    const epc = epc_id == null ? '' : String(epc_id).trim();
    const itemName = name == null ? '' : String(name).trim();
    const normalizedUpc = upc ? normalizeUpc(upc) : null;

    if (!epc) return res.status(400).json({ error: 'epc_id is required' });
    if (!itemName) return res.status(400).json({ error: 'name is required' });

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

// ✏️ Update item metadata (name, description, category)
app.put('/api/items/:epc_id', (req, res) => {
    const { epc_id } = req.params;
    const { name, description, category, home_container_id, upc } = req.body;
    const normalizedHomeContainerId = normalizeContainerId(home_container_id);
    const normalizedUpc = upc === undefined ? undefined : (upc ? normalizeUpc(upc) : null);

    if (!name || typeof name !== 'string' || !name.trim()) {
        return res.status(400).json({ error: 'name is required' });
    }

    const sql = normalizedUpc !== undefined
        ? `UPDATE items SET name = ?, description = ?, category = ?, home_container_id = ?, upc = ? WHERE epc_id = ?`
        : `UPDATE items SET name = ?, description = ?, category = ?, home_container_id = ? WHERE epc_id = ?`;
    const params = normalizedUpc !== undefined
        ? [name.trim(), description ?? null, category ?? null, normalizedHomeContainerId, normalizedUpc, epc_id]
        : [name.trim(), description ?? null, category ?? null, normalizedHomeContainerId, epc_id];

    db.run(sql, params,
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
app.post('/api/containers', (req, res) => {
    const { id, name, description, boundary_tag_a, boundary_tag_b } = req.body;
    const binId = id == null ? '' : String(id).trim();
    const binName = name == null ? '' : String(name).trim();
    const boundaryA = boundary_tag_a == null ? '' : String(boundary_tag_a).trim();
    const boundaryB = boundary_tag_b == null ? '' : String(boundary_tag_b).trim();

    if (!binId) return res.status(400).json({ error: 'id is required' });
    if (!binName) return res.status(400).json({ error: 'name is required' });

    db.run(
        `INSERT INTO containers (id, name, description, boundary_tag_a, boundary_tag_b) VALUES (?, ?, ?, ?, ?)`,
        [binId, binName, description ? String(description).trim() : null, boundaryA || null, boundaryB || null],
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
                boundary_tag_a: boundaryA || null,
                boundary_tag_b: boundaryB || null,
            });
        }
    );
});

function normalizeBoundaryTag(value) {
    const trimmed = value == null ? '' : String(value).trim();
    return trimmed === '' ? null : trimmed;
}

// 📦 Update an existing bin by ID (upsert for scan-time registration)
app.put('/api/containers/:id', (req, res) => {
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