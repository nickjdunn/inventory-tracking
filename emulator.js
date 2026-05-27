/**
 * Nordic ID Merlin UHF — Hardware Client Emulator
 * Simulates bulk inventory scans and Find Mode RSSI polling against the local backend.
 *
 * Usage: node emulator.js
 * Prerequisite: server running at http://localhost:3000
 */

const http = require('http');
const readline = require('readline');

const BASE_URL = 'http://10.17.17.17:3000';
const SCANNER_ID = 'MERLIN-EMU-001';
const FIND_POLL_MS = 1500;

// Mock physical assets (EPC pool)
const MOCK_EPC_POOL = [
    { epc: 'EPC301833B2A1TOOL00001', label: 'Cordless Drill' },
    { epc: 'EPC301833B2A1TOOL00002', label: 'Impact Driver' },
    { epc: 'EPC301833B2A1CABL00003', label: 'HDMI Cable 6ft' },
    { epc: 'EPC301833B2A1CABL00004', label: 'USB-C Hub' },
    { epc: 'EPC301833B2A1GEAR00005', label: 'Camping Headlamp' },
    { epc: 'EPC301833B2A1GEAR00006', label: 'Trekking Poles' },
    { epc: 'EPC301833B2A1TOOL00007', label: 'Socket Set' },
    { epc: 'EPC301833B2A1CABL00008', label: 'Ethernet Cat6 25ft' },
    { epc: 'EPC301833B2A1GEAR00009', label: 'First Aid Kit' },
    { epc: 'EPC301833B2A1TOOL00010', label: 'Stud Finder' },
    { epc: 'EPC301833B2A1GEAR00011', label: 'Bike Pump' },
    { epc: 'EPC301833B2A1CABL00012', label: 'Extension Cord 12ft' },
];

const MOCK_CONTAINERS = [
    { epc: 'BIN-GARAGE-TOOL-001', name: 'Garage Tool Bin' },
    { epc: 'BIN-BASEMENT-CABLE-002', name: 'Basement Cable Tote' },
    { epc: 'BIN-SHED-GEAR-003', name: 'Shed Outdoor Gear' },
];

// Virtual shelf state for drift simulation (epc -> container epc)
const virtualInventory = new Map();

let findLoopTimer = null;
let findLoopRunning = false;
let rssiSimulation = 35;
let lastHuntEpc = null;

// ─── HTTP helpers ───────────────────────────────────────────────────────────

function request(method, path, body = null) {
    return new Promise((resolve, reject) => {
        const url = new URL(path, BASE_URL);
        const payload = body ? JSON.stringify(body) : null;

        const options = {
            hostname: url.hostname,
            port: url.port || 3000,
            path: url.pathname + url.search,
            method,
            headers: {
                'Content-Type': 'application/json',
                Accept: 'application/json',
            },
        };

        if (payload) {
            options.headers['Content-Length'] = Buffer.byteLength(payload);
        }

        const req = http.request(options, (res) => {
            let data = '';
            res.on('data', (chunk) => { data += chunk; });
            res.on('end', () => {
                try {
                    const parsed = data ? JSON.parse(data) : {};
                    if (res.statusCode >= 400) {
                        reject(new Error(parsed.error || parsed.message || `HTTP ${res.statusCode}`));
                    } else {
                        resolve(parsed);
                    }
                } catch {
                    if (res.statusCode >= 400) {
                        reject(new Error(`HTTP ${res.statusCode}: ${data}`));
                    } else {
                        resolve(data);
                    }
                }
            });
        });

        req.on('error', reject);
        if (payload) req.write(payload);
        req.end();
    });
}

function get(path) {
    return request('GET', path);
}

function post(path, body) {
    return request('POST', path, body);
}

// ─── Signal / RSSI visualization ───────────────────────────────────────────

function clamp(n, min, max) {
    return Math.max(min, Math.min(max, n));
}

function renderSignalBar(pct) {
    const p = clamp(Math.round(pct), 0, 100);
    const filled = Math.round(p / 10);
    const empty = 10 - filled;
    return `[${'█'.repeat(filled)}${'░'.repeat(empty)}] ${p}%`;
}

function nextRssiValue() {
    // Random walk with bias toward center — mimics walking closer/farther
    const delta = (Math.random() - 0.48) * 22;
    rssiSimulation = clamp(rssiSimulation + delta, 0, 100);
    return Math.round(rssiSimulation);
}

function pickRandom(arr, count) {
    const copy = [...arr];
    const picked = [];
    const n = Math.min(count, copy.length);
    for (let i = 0; i < n; i++) {
        const idx = Math.floor(Math.random() * copy.length);
        picked.push(copy.splice(idx, 1)[0]);
    }
    return picked;
}

// ─── Inventory Mode ──────────────────────────────────────────────────────────

/**
 * Simulates a Merlin bulk read of tags inside a container bin.
 * Applies occasional drift: missing tags or tags appearing in the wrong bin.
 */
async function simulateInventoryScan(containerEpc, totalTags) {
    const tagCount = clamp(totalTags, 1, MOCK_EPC_POOL.length);
    let selected = pickRandom(MOCK_EPC_POOL, tagCount).map((t) => t.epc);

    // Tags this emulator believes belong in this bin
    const expectedHere = MOCK_EPC_POOL.filter(
        (t) => virtualInventory.get(t.epc) === containerEpc
    ).map((t) => t.epc);

    const driftRoll = Math.random();

    if (driftRoll < 0.2 && expectedHere.length > 0) {
        // Drift: omit one tag that should be here (not seen this scan — dashboard still shows old bin)
        const missing = expectedHere[Math.floor(Math.random() * expectedHere.length)];
        selected = selected.filter((epc) => epc !== missing);
        const meta = MOCK_EPC_POOL.find((t) => t.epc === missing);
        console.log(`\n⚠️  [Drift] Tag NOT read in bin (simulated miss): ${meta?.label || missing}`);
        console.log(`    EPC ${missing} — still assigned until scanned elsewhere.\n`);
    } else if (driftRoll < 0.4) {
        // Drift: include a tag from another virtual bin (physical move)
        const elsewhere = MOCK_EPC_POOL.filter(
            (t) => virtualInventory.has(t.epc) && virtualInventory.get(t.epc) !== containerEpc
        );
        if (elsewhere.length > 0) {
            const stowaway = elsewhere[Math.floor(Math.random() * elsewhere.length)];
            if (!selected.includes(stowaway.epc)) {
                selected.push(stowaway.epc);
                console.log(`\n📦 [Drift] Tag relocated into this bin scan: ${stowaway.label}`);
                console.log(`    Was in [${virtualInventory.get(stowaway.epc)}] → now reporting [${containerEpc}]\n`);
            }
        }
    }

    if (selected.length === 0) {
        selected = pickRandom(MOCK_EPC_POOL, 1).map((t) => t.epc);
    }

    const containerMeta = MOCK_CONTAINERS.find((c) => c.epc === containerEpc);
    console.log('\n─── 📡 Merlin Inventory Scan ───');
    console.log(`   Scanner:    ${SCANNER_ID}`);
    console.log(`   Container:  ${containerMeta?.name || containerEpc}`);
    console.log(`   Tags read:  ${selected.length}`);
    selected.forEach((epc) => {
        const item = MOCK_EPC_POOL.find((t) => t.epc === epc);
        console.log(`   • ${item?.label || 'Unknown'}  [${epc}]`);
    });

    const payload = {
        scanner_id: SCANNER_ID,
        target_container_epc: containerEpc,
        scanned_tags: selected,
    };

    try {
        const result = await post('/api/scan', payload);
        selected.forEach((epc) => virtualInventory.set(epc, containerEpc));
        console.log(`\n✅ Backend: ${result.message || result.status}`);
        console.log('─── Scan complete ───\n');
    } catch (err) {
        console.error(`\n❌ Scan POST failed: ${err.message}`);
        console.error('   Is the server running? (npm start)\n');
    }
}

// ─── Find Mode polling loop ──────────────────────────────────────────────────

async function findModePollTick() {
    try {
        const { activeSearchEpc } = await get('/api/search/target');

        if (!activeSearchEpc) {
            if (lastHuntEpc) {
                console.log('\n🔍 [Merlin Hardware] Find Mode idle — no dashboard target.\n');
                lastHuntEpc = null;
                rssiSimulation = 35;
            }
            return;
        }

        if (activeSearchEpc !== lastHuntEpc) {
            lastHuntEpc = activeSearchEpc;
            rssiSimulation = 15 + Math.floor(Math.random() * 25);
        }

        const rssi = nextRssiValue();
        const bar = renderSignalBar(rssi);
        const proximity =
            rssi >= 75 ? 'VERY CLOSE' : rssi >= 50 ? 'NEARBY' : rssi >= 25 ? 'WEAK' : 'DISTANT';

        console.log(`🔍 [Merlin Hardware] Hunting for target: ${activeSearchEpc}`);
        console.log(`   RSSI ${bar}  (${proximity})`);
    } catch (err) {
        console.error(`🔍 [Merlin Hardware] Poll error: ${err.message}`);
    }
}

function startFindModeLoop() {
    if (findLoopRunning) {
        console.log('\nℹ️  Find Mode polling is already running.\n');
        return;
    }
    findLoopRunning = true;
    console.log(`\n▶️  Find Mode polling started (every ${FIND_POLL_MS}ms)\n`);
    findModePollTick();
    findLoopTimer = setInterval(findModePollTick, FIND_POLL_MS);
}

function stopFindModeLoop() {
    if (!findLoopRunning) {
        console.log('\nℹ️  Find Mode polling is not running.\n');
        return;
    }
    clearInterval(findLoopTimer);
    findLoopTimer = null;
    findLoopRunning = false;
    lastHuntEpc = null;
    console.log('\n⏹️  Find Mode polling stopped.\n');
}

// ─── CLI menu ────────────────────────────────────────────────────────────────

function printBanner() {
    console.log(`
╔══════════════════════════════════════════════════════════╗
║   Nordic ID Merlin UHF — Hardware Client Emulator        ║
║   Backend: ${BASE_URL.padEnd(43)}║
╚══════════════════════════════════════════════════════════╝
`);
}

function printMenu() {
    const findStatus = findLoopRunning ? 'ON — press to stop' : 'OFF — press to start';
    console.log(`
┌─────────────────────────────────────────┐
│  1. Trigger Bulk Bin Scan               │
│  2. Find Mode Polling Loop (${findStatus}) │
│  3. Exit                                │
└─────────────────────────────────────────┘
`);
}

function toggleFindModeLoop() {
    if (findLoopRunning) {
        stopFindModeLoop();
    } else {
        startFindModeLoop();
    }
}

function seedVirtualInventory() {
    MOCK_EPC_POOL.forEach((tag, i) => {
        const bin = MOCK_CONTAINERS[i % MOCK_CONTAINERS.length].epc;
        virtualInventory.set(tag.epc, bin);
    });
}

function prompt(rl, question) {
    return new Promise((resolve) => rl.question(question, resolve));
}

async function handleBulkScan(rl) {
    console.log('\nAvailable mock containers:');
    MOCK_CONTAINERS.forEach((c, i) => {
        console.log(`  ${i + 1}. ${c.name}  [${c.epc}]`);
    });
    const containerChoice = await prompt(rl, '\nSelect container (1-3) or Enter for random: ');
    let containerEpc;
    const idx = parseInt(containerChoice, 10);
    if (idx >= 1 && idx <= MOCK_CONTAINERS.length) {
        containerEpc = MOCK_CONTAINERS[idx - 1].epc;
    } else {
        const pick = MOCK_CONTAINERS[Math.floor(Math.random() * MOCK_CONTAINERS.length)];
        containerEpc = pick.epc;
        console.log(`   → Random bin: ${pick.name}`);
    }

    const countInput = await prompt(rl, `Tag count to read (1-${MOCK_EPC_POOL.length}, default 5): `);
    const totalTags = countInput.trim()
        ? clamp(parseInt(countInput, 10) || 5, 1, MOCK_EPC_POOL.length)
        : 5;

    await simulateInventoryScan(containerEpc, totalTags);
}

async function runCli() {
    printBanner();
    seedVirtualInventory();
    console.log('💡 Tip: Open the dashboard, set Find Mode on an item, then start option 2 here.\n');

    const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout,
    });

    const menuLoop = async () => {
        printMenu();
        const choice = await prompt(rl, 'Select option: ');

        switch (choice.trim()) {
            case '1':
                await handleBulkScan(rl);
                break;
            case '2':
                toggleFindModeLoop();
                break;
            case '3':
                stopFindModeLoop();
                rl.close();
                console.log('\n👋 Merlin emulator shut down.\n');
                process.exit(0);
                return;
            default:
                console.log('\n⚠️  Invalid option. Choose 1-3.\n');
        }

        menuLoop();
    };

    rl.on('close', () => {
        stopFindModeLoop();
        process.exit(0);
    });

    await menuLoop();
}

runCli().catch((err) => {
    console.error('Fatal:', err);
    process.exit(1);
});
