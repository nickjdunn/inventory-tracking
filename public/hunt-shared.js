/**
 * Shared hunt radar helpers (ES5-safe for Win CE + modern mobile).
 */
(function (global) {
    function trimStr(s) {
        if (s == null) return '';
        return String(s).replace(/^\s+|\s+$/g, '');
    }

    function zoneMeterClass(zone) {
        var z = zone == null ? '' : String(zone).toUpperCase();
        if (z === 'CLOSE') return 'zone-close';
        if (z === 'WARM') return 'zone-warm';
        if (z === 'COLD') return 'zone-cold';
        return 'zone-none';
    }

    function normalizeZoneLabel(zone) {
        if (!zone) return 'NO SIGNAL';
        return String(zone).replace(/_/g, ' ');
    }

    function dbmToPct(dbm, nearGate, farGate) {
        var span = nearGate - farGate;
        if (span <= 0) span = 1;
        var pct = Math.round(((dbm - farGate) / span) * 100);
        if (pct < 0) return 0;
        if (pct > 100) return 100;
        return pct;
    }

    function findSignalForEpc(huntTargets, epc) {
        if (!huntTargets || !huntTargets.length || !epc) return null;
        var targetEpc = trimStr(epc).toUpperCase();
        var i;
        for (i = 0; i < huntTargets.length; i++) {
            var row = huntTargets[i];
            if (!row || !row.epc) continue;
            if (trimStr(row.epc).toUpperCase() === targetEpc) return row;
        }
        return null;
    }

    function indexOfEpc(queue, epc) {
        if (!queue || !queue.length) return -1;
        var needle = trimStr(epc).toUpperCase();
        var i;
        for (i = 0; i < queue.length; i++) {
            if (trimStr(queue[i]).toUpperCase() === needle) return i;
        }
        return -1;
    }

    function meterHeightPct(signal, nearGate, farGate) {
        if (!signal || signal.rssi == null || isNaN(signal.rssi)) return 0;
        return dbmToPct(signal.rssi, nearGate, farGate);
    }

    function dbmDisplay(signal) {
        if (!signal || signal.rssi == null || isNaN(signal.rssi)) return '— dBm';
        return String(signal.rssi) + ' dBm';
    }

    global.MerlinHunt = {
        zoneMeterClass: zoneMeterClass,
        normalizeZoneLabel: normalizeZoneLabel,
        dbmToPct: dbmToPct,
        findSignalForEpc: findSignalForEpc,
        indexOfEpc: indexOfEpc,
        meterHeightPct: meterHeightPct,
        dbmDisplay: dbmDisplay,
        trimStr: trimStr,
    };
})(typeof window !== 'undefined' ? window : global);
