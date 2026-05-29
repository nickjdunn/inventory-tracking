/**
 * Shared inventory filtering, status computation, and badge rendering
 * for desktop (index.html) and mobile (mobile.html) parity.
 */
(function (global) {
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

    function enrichItems(items) {
        return (items || []).map((item) => ({
            ...item,
            status: item.status || computeItemStatus(item),
        }));
    }

    function matchesSearch(item, query) {
        const q = (query || '').trim().toLowerCase();
        if (!q) return true;
        const name = (item.name || '').toLowerCase();
        const epc = (item.epc_id || '').toLowerCase();
        const upc = (item.upc || '').toLowerCase();
        const containerName = (item.container_name || '').toLowerCase();
        const containerId = (item.container_id || '').toLowerCase();
        const homeName = (item.home_container_name || '').toLowerCase();
        return (
            name.includes(q) ||
            epc.includes(q) ||
            upc.includes(q) ||
            containerName.includes(q) ||
            containerId.includes(q) ||
            homeName.includes(q)
        );
    }

    function filterItems(items, options) {
        const { query = '', statusFilter = 'ALL' } = options || {};
        return enrichItems(items).filter((item) => {
            if (statusFilter !== 'ALL' && item.status !== statusFilter) return false;
            return matchesSearch(item, query);
        });
    }

    function countByStatus(items) {
        const enriched = enrichItems(items);
        const counts = { ALL: enriched.length, HOME: 0, FLOATING: 0, MISPLACED: 0, UNASSIGNED: 0 };
        enriched.forEach((item) => {
            if (counts[item.status] !== undefined) counts[item.status] += 1;
        });
        return counts;
    }

    function renderStatusBadgeHtml(item, escapeHtml) {
        const esc = escapeHtml || ((s) => String(s ?? ''));
        if (item.status === 'HOME') {
            return '<span class="status-pill status-home">🟢 Home</span>';
        }
        if (item.status === 'FLOATING') {
            return '<span class="status-pill status-floating">🟡 Missing</span>';
        }
        if (item.status === 'MISPLACED') {
            const homeLabel = item.home_container_name || item.home_container_id || 'Unknown Home Bin';
            return (
                '<span class="status-pill status-misplaced">🔴 Misplaced</span>' +
                '<span class="status-detail">Belongs in ' + esc(homeLabel) + '</span>'
            );
        }
        return '<span class="status-pill status-unassigned">⚪ Unassigned</span>';
    }

    const STATUS_PILLS = [
        { id: 'ALL', label: 'All' },
        { id: 'HOME', label: '🟢 Home' },
        { id: 'FLOATING', label: '🟡 Missing' },
        { id: 'MISPLACED', label: '🔴 Misplaced' },
        { id: 'UNASSIGNED', label: '⚪ Unassigned' },
    ];

    let audioCtx = null;

    async function unlockAudio() {
        if (!audioCtx) {
            const Ctx = global.AudioContext || global.webkitAudioContext;
            if (Ctx) audioCtx = new Ctx();
        }
        if (audioCtx && audioCtx.state === 'suspended') await audioCtx.resume();
    }

    function playHomeConfirmTone() {
        if (!audioCtx || audioCtx.state !== 'running') return;
        const t0 = audioCtx.currentTime;
        [659.25, 880].forEach((freq, i) => {
            const osc = audioCtx.createOscillator();
            const g = audioCtx.createGain();
            osc.type = 'sine';
            osc.frequency.setValueAtTime(freq, t0 + i * 0.08);
            g.gain.setValueAtTime(0.0001, t0 + i * 0.08);
            g.gain.exponentialRampToValueAtTime(0.1, t0 + i * 0.08 + 0.02);
            g.gain.exponentialRampToValueAtTime(0.0001, t0 + i * 0.08 + 0.14);
            osc.connect(g);
            g.connect(audioCtx.destination);
            osc.start(t0 + i * 0.08);
            osc.stop(t0 + i * 0.08 + 0.16);
        });
    }

    function canSetCurrentBinAsHome(item) {
        const currentId = normalizeContainerId(item.container_id);
        const homeId = normalizeContainerId(item.home_container_id);
        return Boolean(currentId && currentId !== homeId);
    }

    async function setCurrentBinAsHome(item) {
        const currentId = normalizeContainerId(item.container_id);
        if (!currentId) {
            throw new Error('Item has no current bin location');
        }

        const res = await fetch('/api/items/' + encodeURIComponent(item.epc_id), {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                name: item.name,
                description: item.description ?? null,
                category: item.category ?? null,
                home_container_id: currentId,
                container_id: currentId,
            }),
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.error || 'Failed to set home bin');

        await unlockAudio();
        playHomeConfirmTone();

        return enrichItems([{ ...item, home_container_id: currentId, container_id: currentId }])[0];
    }

    global.MerlinInventory = {
        normalizeContainerId,
        computeItemStatus,
        enrichItems,
        matchesSearch,
        filterItems,
        countByStatus,
        renderStatusBadgeHtml,
        unlockAudio,
        playHomeConfirmTone,
        canSetCurrentBinAsHome,
        setCurrentBinAsHome,
        STATUS_PILLS,
    };
})(typeof window !== 'undefined' ? window : global);
