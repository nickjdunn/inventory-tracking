/**
 * Nordic ID Merlin — Proxmox inventory backend client
 *
 * Lightweight dispatch layer for handheld trigger pulls, near-field onboarding,
 * and connectivity heartbeats. Runs on Node.js 18+ (native fetch) or falls back
 * to http/https for older embedded runtimes.
 *
 * Usage:
 *   const merlin = require('./hardware/merlin-client');
 *   merlin.startHeartbeat();
 *   await merlin.sendBulkInventoryScan([{ epc: 'EPC…', rssi: -48 }]);
 *   await merlin.sendNearFieldCapture('EPC…', -42);
 */

'use strict';

const http = require('http');
const https = require('https');

// ─── Configuration (edit for your site) ─────────────────────────────────────

const CONFIG = {
    SERVER_IP: process.env.MERLIN_SERVER_IP || '10.17.17.17',
    SERVER_PORT: parseInt(process.env.MERLIN_SERVER_PORT || '3000', 10),
    SCANNER_ID: process.env.MERLIN_SCANNER_ID || 'HTE00072',
    /** Optional default bin for bulk scans when not passed per-call */
    TARGET_CONTAINER_EPC: process.env.MERLIN_TARGET_BIN || '',
    HEARTBEAT_INTERVAL_MS: 30_000,
    REQUEST_TIMEOUT_MS: 12_000,
    MAX_RETRIES: 3,
    RETRY_BASE_DELAY_MS: 800,
};

// ─── Internal state ─────────────────────────────────────────────────────────

let heartbeatTimer = null;
let lastDispatchError = null;

function getBaseUrl() {
    return `http://${CONFIG.SERVER_IP}:${CONFIG.SERVER_PORT}`;
}

function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

function normalizeTagEntry(raw) {
    if (typeof raw === 'string') {
        return { epc: raw.trim(), rssi: null };
    }
    if (raw && typeof raw === 'object') {
        const epc = String(raw.epc ?? raw.EPC ?? raw.tag ?? raw.id ?? '').trim();
        const rssiRaw = raw.rssi ?? raw.RSSI ?? raw.signal ?? null;
        const rssi = rssiRaw == null ? null : parseInt(String(rssiRaw), 10);
        return {
            epc,
            rssi: Number.isNaN(rssi) ? null : rssi,
        };
    }
    return { epc: '', rssi: null };
}

function normalizeTagArray(rawTagArray) {
    if (!Array.isArray(rawTagArray)) return [];
    return rawTagArray.map(normalizeTagEntry).filter((t) => t.epc);
}

/**
 * Low-level HTTP dispatch with retries (Wi-Fi handoff tolerant).
 */
function requestHttp(method, path, body) {
    return new Promise((resolve, reject) => {
        const url = new URL(path, getBaseUrl());
        const payload = body ? JSON.stringify(body) : null;
        const lib = url.protocol === 'https:' ? https : http;

        const options = {
            hostname: url.hostname,
            port: url.port || CONFIG.SERVER_PORT,
            path: url.pathname + url.search,
            method,
            headers: {
                Accept: 'application/json',
                'Content-Type': 'application/json',
            },
            timeout: CONFIG.REQUEST_TIMEOUT_MS,
        };

        if (payload) {
            options.headers['Content-Length'] = Buffer.byteLength(payload);
        }

        const req = lib.request(options, (res) => {
            let data = '';
            res.on('data', (chunk) => {
                data += chunk;
            });
            res.on('end', () => {
                let parsed = {};
                try {
                    parsed = data ? JSON.parse(data) : {};
                } catch {
                    parsed = { raw: data };
                }
                if (res.statusCode >= 400) {
                    const err = new Error(parsed.error || parsed.message || `HTTP ${res.statusCode}`);
                    err.statusCode = res.statusCode;
                    err.body = parsed;
                    reject(err);
                } else {
                    resolve(parsed);
                }
            });
        });

        req.on('timeout', () => {
            req.destroy();
            reject(new Error('Request timed out'));
        });

        req.on('error', reject);

        if (payload) req.write(payload);
        req.end();
    });
}

async function requestFetch(method, path, body) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), CONFIG.REQUEST_TIMEOUT_MS);

    try {
        const res = await fetch(new URL(path, getBaseUrl()).toString(), {
            method,
            headers: {
                Accept: 'application/json',
                'Content-Type': 'application/json',
            },
            body: body ? JSON.stringify(body) : undefined,
            signal: controller.signal,
        });

        const parsed = await res.json().catch(() => ({}));
        if (!res.ok) {
            const err = new Error(parsed.error || parsed.message || `HTTP ${res.status}`);
            err.statusCode = res.status;
            err.body = parsed;
            throw err;
        }
        return parsed;
    } finally {
        clearTimeout(timeout);
    }
}

async function dispatch(method, path, body) {
    let lastErr;

    for (let attempt = 0; attempt < CONFIG.MAX_RETRIES; attempt++) {
        try {
            const result =
                typeof fetch === 'function'
                    ? await requestFetch(method, path, body)
                    : await requestHttp(method, path, body);
            lastDispatchError = null;
            return result;
        } catch (err) {
            lastErr = err;
            lastDispatchError = err;
            if (attempt < CONFIG.MAX_RETRIES - 1) {
                const delay = CONFIG.RETRY_BASE_DELAY_MS * Math.pow(2, attempt);
                console.warn(
                    `[Merlin] ${method} ${path} failed (attempt ${attempt + 1}/${CONFIG.MAX_RETRIES}): ${err.message} — retry in ${delay}ms`
                );
                await sleep(delay);
            }
        }
    }

    throw lastErr;
}

// ─── Public API ─────────────────────────────────────────────────────────────

/**
 * MODE 1 — Bulk inventory scan (physical trigger hold).
 *
 * @param {Array<string|{ epc: string, rssi?: number }>} rawTagArray
 * @param {{ targetContainerEpc?: string }} [options]
 * @returns {Promise<object>} Server JSON response
 */
async function sendBulkInventoryScan(rawTagArray, options = {}) {
    const tags = normalizeTagArray(rawTagArray);
    if (!tags.length) {
        throw new Error('sendBulkInventoryScan: no valid tags in array');
    }

    const targetContainerEpc =
        options.targetContainerEpc ||
        options.target_container_epc ||
        CONFIG.TARGET_CONTAINER_EPC ||
        null;

    const payload = {
        scanner_id: CONFIG.SCANNER_ID,
        scannerId: CONFIG.SCANNER_ID,
        target_container_epc: targetContainerEpc,
        targetContainerEpc,
        scanned_tags: tags,
        tags,
    };

    console.log(`[Merlin] Bulk scan → ${tags.length} tag(s) → ${getBaseUrl()}/api/scan`);
    return dispatch('POST', '/api/scan', payload);
}

/**
 * Keyboard wedge / raw stream from Merlin native software.
 *
 * @param {string|object|Array} rawPayload Plain text, JSON body, or tag array
 * @param {{ targetContainerEpc?: string }} [options]
 */
async function sendMerlinWedge(rawPayload, options = {}) {
    const targetContainerEpc =
        options.targetContainerEpc ||
        options.target_container_epc ||
        CONFIG.TARGET_CONTAINER_EPC ||
        null;

    let body;
    if (typeof rawPayload === 'string') {
        body = { raw: rawPayload, target_container_epc: targetContainerEpc, scanner_id: CONFIG.SCANNER_ID };
    } else if (Array.isArray(rawPayload)) {
        body = {
            scanned_tags: normalizeTagArray(rawPayload),
            target_container_epc: targetContainerEpc,
            scanner_id: CONFIG.SCANNER_ID,
        };
    } else {
        body = {
            ...(rawPayload && typeof rawPayload === 'object' ? rawPayload : {}),
            target_container_epc: targetContainerEpc,
            scanner_id: CONFIG.SCANNER_ID,
        };
    }

    console.log(`[Merlin] Wedge ingest → ${getBaseUrl()}/api/hardware/merlin-wedge`);
    return dispatch('POST', '/api/hardware/merlin-wedge', body);
}

/**
 * MODE 2 — Ultra-near single tag capture (onboarding / quick-register mode).
 *
 * @param {string} epc
 * @param {number} rssi dBm (must exceed admin rssi_near_gate on server)
 * @returns {Promise<object>}
 */
async function sendNearFieldCapture(epc, rssi) {
    const normalizedEpc = String(epc ?? '').trim();
    if (!normalizedEpc) {
        throw new Error('sendNearFieldCapture: epc is required');
    }
    const rssiNum = parseInt(String(rssi), 10);
    if (Number.isNaN(rssiNum)) {
        throw new Error('sendNearFieldCapture: rssi must be a number');
    }

    const tag = { epc: normalizedEpc, rssi: rssiNum };
    const payload = {
        scanner_id: CONFIG.SCANNER_ID,
        scanned_tags: [tag],
        tags: [tag],
    };

    console.log(`[Merlin] Near-field → ${normalizedEpc} @ ${rssiNum} dBm`);
    return dispatch('POST', '/api/scan/near-field-ingest', payload);
}

/**
 * Heartbeat ping — keeps dashboard "scanner online" badge fresh.
 */
/**
 * Pull compact inventory + bins + hunt queue for native handheld cache.
 * @returns {Promise<object>}
 */
async function fetchHandheldSync() {
    return dispatch('GET', '/api/handheld/sync');
}

async function sendHeartbeat(extra = {}) {
    const payload = {
        scanner_id: CONFIG.SCANNER_ID,
        timestamp: Date.now(),
        ...extra,
    };
    return dispatch('POST', '/api/scanner/heartbeat', payload);
}

function startHeartbeat() {
    if (heartbeatTimer) return;

    const tick = async () => {
        try {
            await sendHeartbeat();
        } catch (err) {
            console.warn('[Merlin] Heartbeat failed:', err.message);
        }
    };

    tick();
    heartbeatTimer = setInterval(tick, CONFIG.HEARTBEAT_INTERVAL_MS);
    console.log(
        `[Merlin] Heartbeat started (${CONFIG.HEARTBEAT_INTERVAL_MS / 1000}s) → ${getBaseUrl()}`
    );
}

function stopHeartbeat() {
    if (heartbeatTimer) {
        clearInterval(heartbeatTimer);
        heartbeatTimer = null;
        console.log('[Merlin] Heartbeat stopped');
    }
}

function getConfig() {
    return { ...CONFIG };
}

function getLastError() {
    return lastDispatchError;
}

// ─── CLI: node hardware/merlin-client.js ────────────────────────────────────

if (require.main === module) {
    console.log('[Merlin] Client idle — heartbeat only. Import functions for scans.');
    console.log(`[Merlin] Backend: ${getBaseUrl()}  Scanner: ${CONFIG.SCANNER_ID}`);
    startHeartbeat();

    process.on('SIGINT', () => {
        stopHeartbeat();
        process.exit(0);
    });
}

module.exports = {
    CONFIG,
    getConfig,
    getBaseUrl,
    sendBulkInventoryScan,
    sendMerlinWedge,
    sendNearFieldCapture,
    fetchHandheldSync,
    sendHeartbeat,
    startHeartbeat,
    stopHeartbeat,
    getLastError,
    normalizeTagArray,
};
