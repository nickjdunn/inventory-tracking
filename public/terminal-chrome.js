/**
 * Shared footer nav + version for deploy / terminal pages (no sidebar).
 */
(function () {
    var LINKS = [
        { href: '/deploy/', label: '📡 Deploy hub' },
        { href: '/deploy/scanner-live.html', label: '📡 Live scanner feed' },
        { href: '/deploy/ce-wifi-test.html', label: '📶 Wi‑Fi test' },
        { href: '/mobile.html', label: '📱 Mobile UI' },
        { href: '/index.html?desktop=1', label: '🖥 Desktop' },
    ];

    function mount() {
        var el = document.querySelector('[data-terminal-chrome]');
        if (!el) return;

        var nav = document.createElement('nav');
        nav.className = 'terminal-chrome-nav';
        nav.setAttribute('aria-label', 'Site navigation');

        for (var i = 0; i < LINKS.length; i++) {
            var a = document.createElement('a');
            a.href = LINKS[i].href;
            a.textContent = LINKS[i].label;
            if (location.pathname === LINKS[i].href.replace(/\?.*$/, '')) {
                a.className = 'active';
            }
            nav.appendChild(a);
        }

        var ver = document.createElement('p');
        ver.className = 'site-version-line';
        ver.setAttribute('data-site-version', '');
        ver.textContent = 'Loading version…';

        el.appendChild(nav);
        el.appendChild(ver);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', mount);
    } else {
        mount();
    }
})();
