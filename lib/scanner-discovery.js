const { exec } = require('child_process');

const SCAN_CACHE_MS = 60_000;
let lastScan = { at: 0, hosts: [], subnet: '' };

function parseIpv4(ip) {
    const m = String(ip || '').match(/^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/);
    if (!m) return null;
    const parts = m.slice(1, 5).map((n) => parseInt(n, 10));
    if (parts.some((n) => n < 0 || n > 255)) return null;
    return parts;
}

function subnetBaseFromIp(ip) {
    const parts = parseIpv4(ip);
    if (!parts) return null;
    return `${parts[0]}.${parts[1]}.${parts[2]}`;
}

function pingHost(ip) {
    return new Promise((resolve) => {
        const isWin = process.platform === 'win32';
        const cmd = isWin
            ? `ping -n 1 -w 500 ${ip}`
            : `ping -c 1 -W 1 ${ip}`;
        exec(cmd, { timeout: 1500 }, (err) => {
            resolve(!err);
        });
    });
}

/**
 * Quick LAN sweep (last octet 1–254). Best-effort; may take ~15–25s.
 */
async function scanSubnet(base, options = {}) {
    const maxHosts = options.maxHosts || 64;
    const hosts = [];
    const tasks = [];
    let started = 0;

    for (let last = 1; last <= 254 && started < maxHosts; last++) {
        const ip = `${base}.${last}`;
        started++;
        tasks.push(
            pingHost(ip).then((alive) => {
                if (alive) hosts.push(ip);
            })
        );
        if (tasks.length >= 32) {
            await Promise.all(tasks);
            tasks.length = 0;
        }
    }
    if (tasks.length) await Promise.all(tasks);
    return hosts.sort();
}

async function discoverLanHosts(serverIp, force) {
    const base = subnetBaseFromIp(serverIp);
    if (!base) return { subnet: null, hosts: [], note: 'Could not derive subnet from server IP' };

    const now = Date.now();
    if (!force && lastScan.subnet === base && now - lastScan.at < SCAN_CACHE_MS) {
        return { subnet: base, hosts: lastScan.hosts, cached: true };
    }

    const hosts = await scanSubnet(base);
    lastScan = { at: now, hosts, subnet: base };
    return { subnet: base, hosts, cached: false };
}

module.exports = {
    discoverLanHosts,
    subnetBaseFromIp,
    parseIpv4,
};
