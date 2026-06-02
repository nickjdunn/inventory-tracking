/**
 * Shared sidebar navigation for desktop inventory pages.
 * Set <body data-nav-page="dashboard|bins|diagnostics|discovery|admin|emulator">
 * and <aside class="sidebar" data-app-sidebar></aside>
 */
(function () {
    var BRAND = {
        title: '🧠 RFID Inventory',
        subtitle: 'Merlin warehouse system',
    };

    var NAV_MAIN = [
        { id: 'dashboard', href: '/index.html', label: '📦 Inventory Dashboard' },
        { id: 'bins', href: '/bins.html', label: '🗃️ Bin & Location Manager' },
        { id: 'diagnostics', href: '/diagnostics.html', label: '🧪 Spatial Diagnostics' },
        { id: 'discovery', href: '/discovery.html', label: '🏴 Rogue Tag Discovery' },
        { id: 'admin', href: '/admin.html', label: '⚙️ Admin Settings' },
    ];

    var NAV_TOOLS = [
        { id: 'emulator', href: '/emulator.html', label: '📡 Hardware Simulator' },
    ];

    var NAV_FOOTER = [
        { href: '/mobile.html', label: '📱 Handheld mobile UI' },
        { href: '/deploy/', label: '📡 Scanner deploy hub' },
        { href: '/deploy/scanner-live.html', label: '📡 Live scanner feed' },
        { href: '/deploy/ce-wifi-test.html', label: '📶 Wi‑Fi API test' },
    ];

    function renderSidebar(mount, activeId) {
        var mainLinks = NAV_MAIN.map(function (item) {
            var cls = 'sidebar-link' + (item.id === activeId ? ' active' : '');
            return '<a class="' + cls + '" href="' + item.href + '">' + item.label + '</a>';
        }).join('');

        var toolLinks = NAV_TOOLS.map(function (item) {
            var cls = 'sidebar-link sidebar-link-tool' + (item.id === activeId ? ' active' : '');
            return '<a class="' + cls + '" href="' + item.href + '">' + item.label + '</a>';
        }).join('');

        var footerLinks = NAV_FOOTER.map(function (item) {
            return '<a class="sidebar-footer-link" href="' + item.href + '">' + item.label + '</a>';
        }).join('');

        mount.innerHTML =
            '<div class="sidebar-brand">' +
            '<h1>' + BRAND.title + '</h1>' +
            '<p>' + BRAND.subtitle + '</p>' +
            '</div>' +
            '<nav class="sidebar-nav" aria-label="Main navigation">' +
            mainLinks +
            '</nav>' +
            '<nav class="sidebar-nav sidebar-nav-tools" aria-label="Tools">' +
            '<p class="sidebar-nav-label">Tools</p>' +
            toolLinks +
            '</nav>' +
            '<div class="sidebar-footer">' +
            footerLinks +
            '</div>' +
            '<p class="sidebar-version" data-site-version>Loading version…</p>';
    }

    function init() {
        var activeId = document.body.getAttribute('data-nav-page') || '';
        var mounts = document.querySelectorAll('[data-app-sidebar]');
        for (var i = 0; i < mounts.length; i++) {
            renderSidebar(mounts[i], activeId);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
