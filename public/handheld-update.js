/**
 * In-app / in-browser update checks for Merlin handheld (native CAB + web UI).
 * Requires GET /api/deploy/info (JSON or JSONP via ?callback=).
 */
(function (global) {
    var STORAGE_KEY = 'merlin_handheld_client_version';

    function parseVersion(version) {
        if (!version) return [0, 0, 0];
        var main = String(version).split('+')[0].split('-')[0];
        var parts = main.split('.');
        return [
            parseInt(parts[0], 10) || 0,
            parseInt(parts[1], 10) || 0,
            parseInt(parts[2], 10) || 0,
        ];
    }

    function compareVersion(a, b) {
        var pa = parseVersion(a);
        var pb = parseVersion(b);
        for (var i = 0; i < 3; i++) {
            if (pa[i] !== pb[i]) return pa[i] < pb[i] ? -1 : 1;
        }
        return 0;
    }

    function getStoredClientVersion() {
        try {
            return global.localStorage ? global.localStorage.getItem(STORAGE_KEY) || '' : '';
        } catch (e) {
            return '';
        }
    }

    function setStoredClientVersion(v) {
        try {
            if (global.localStorage && v) global.localStorage.setItem(STORAGE_KEY, v);
        } catch (e) {}
    }

    function fetchDeployInfo(baseUrl, callback) {
        var base = (baseUrl || '').replace(/\/$/, '');
        if (!base) base = '';
        var url = base + '/api/deploy/info?_=' + new Date().getTime();

        if (typeof global.jsonpGet === 'function') {
            global.jsonpGet(url, function (ok, st, text, data) {
                if (ok && data) return callback(null, data);
                try {
                    callback(null, JSON.parse(text));
                } catch (e) {
                    callback(e || new Error('deploy info failed'));
                }
            });
            return;
        }

        try {
            var x = new global.XMLHttpRequest();
            x.open('GET', url, true);
            x.onreadystatechange = function () {
                if (x.readyState !== 4) return;
                if (x.status >= 200 && x.status < 300) {
                    try {
                        callback(null, JSON.parse(x.responseText || '{}'));
                    } catch (e) {
                        callback(e);
                    }
                } else {
                    callback(new Error('HTTP ' + x.status));
                }
            };
            x.send(null);
        } catch (err) {
            callback(err);
        }
    }

    /**
     * @param {object} opts
     * @param {string} opts.clientVersion - this page/app version (empty = use localStorage)
     * @param {string} [opts.baseUrl] - server root
     * @param {function} opts.onResult - fn(err, { updateAvailable, server, client, info })
     */
    function checkForUpdate(opts) {
        opts = opts || {};
        var client =
            opts.clientVersion != null && opts.clientVersion !== ''
                ? String(opts.clientVersion)
                : getStoredClientVersion();

        fetchDeployInfo(opts.baseUrl, function (err, info) {
            if (err) return opts.onResult(err, null);
            var serverVer = info.version || '';
            var updateAvailable = compareVersion(serverVer, client) > 0;
            if (!updateAvailable && serverVer) {
                setStoredClientVersion(serverVer);
            }
            opts.onResult(null, {
                updateAvailable: updateAvailable,
                server: serverVer,
                client: client,
                info: info,
            });
        });
    }

    function formatUpdateMessage(result) {
        if (!result || !result.updateAvailable) return '';
        var lines = [
            'Update available',
            'Installed: ' + (result.client || '(unknown)'),
            'Server:    ' + result.server,
        ];
        if (result.info && result.info.cab_available) {
            lines.push('Download the new .cab from the deploy page.');
        } else {
            lines.push('Refresh this page after the server is updated.');
        }
        return lines.join('\n');
    }

    global.MerlinHandheldUpdate = {
        STORAGE_KEY: STORAGE_KEY,
        parseVersion: parseVersion,
        compareVersion: compareVersion,
        getStoredClientVersion: getStoredClientVersion,
        setStoredClientVersion: setStoredClientVersion,
        checkForUpdate: checkForUpdate,
        formatUpdateMessage: formatUpdateMessage,
    };
})(typeof window !== 'undefined' ? window : global);
