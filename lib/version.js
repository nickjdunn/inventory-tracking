/**
 * Parse and compare Merlin handheld version strings (1.0.42+abc1234).
 */

function parseVersion(version) {
    if (version == null || version === '') return [0, 0, 0];
    const main = String(version).split('+')[0].split('-')[0];
    const parts = main.split('.').map((p) => parseInt(p, 10) || 0);
    while (parts.length < 3) parts.push(0);
    return parts.slice(0, 3);
}

/** @returns {number} negative if a < b, positive if a > b, 0 if equal */
function compareVersion(a, b) {
    const pa = parseVersion(a);
    const pb = parseVersion(b);
    for (let i = 0; i < 3; i++) {
        if (pa[i] !== pb[i]) return pa[i] < pb[i] ? -1 : 1;
    }
    return 0;
}

function isNewerVersion(serverVersion, clientVersion) {
    return compareVersion(serverVersion, clientVersion) > 0;
}

module.exports = { parseVersion, compareVersion, isNewerVersion };
