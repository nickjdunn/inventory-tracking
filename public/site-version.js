/**
 * Fills elements with [data-site-version] from /version.generated.json
 */
(function () {
    function applyVersion(meta) {
        var text = meta && meta.version ? 'Server ' + meta.version : 'Server version unknown';
        var nodes = document.querySelectorAll('[data-site-version]');
        for (var i = 0; i < nodes.length; i++) {
            nodes[i].textContent = text;
        }
    }

    function load() {
        fetch('/version.generated.json?_=' + Date.now())
            .then(function (r) {
                return r.json();
            })
            .then(applyVersion)
            .catch(function () {
                applyVersion(null);
            });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', load);
    } else {
        load();
    }
})();
