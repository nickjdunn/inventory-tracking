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

    global.MerlinInventory = {
        normalizeContainerId,
        computeItemStatus,
        enrichItems,
        matchesSearch,
        filterItems,
        countByStatus,
        renderStatusBadgeHtml,
        STATUS_PILLS,
    };
})(typeof window !== 'undefined' ? window : global);
