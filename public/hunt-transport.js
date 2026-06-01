/**
 * Low-latency hunt transport: WebSocket → SSE → long-poll → fast compact poll (ES5).
 */
(function (global) {
    function createHuntTransport(options) {
        var opts = options || {};
        var onPayload = opts.onPayload || function () {};
        var onMode = opts.onMode || function () {};
        var preferWebSocket = opts.preferWebSocket !== false;
        var stopped = false;
        var huntRevision = 0;
        var ws = null;
        var es = null;
        var fastPollTimer = null;
        var longPollXhr = null;

        function handlePayload(data) {
            if (!data || stopped) return;
            if (data.revision != null) huntRevision = data.revision;
            onPayload(data);
        }

        function stopAll() {
            stopped = true;
            if (ws) {
                try {
                    ws.onclose = null;
                    ws.close();
                } catch (e1) {}
                ws = null;
            }
            if (es) {
                try {
                    es.close();
                } catch (e2) {}
                es = null;
            }
            if (fastPollTimer) {
                clearInterval(fastPollTimer);
                fastPollTimer = null;
            }
            if (longPollXhr) {
                try {
                    longPollXhr.abort();
                } catch (e3) {}
                longPollXhr = null;
            }
        }

        function xhrJson(method, url, callback) {
            var xhr;
            try {
                xhr = new XMLHttpRequest();
            } catch (e) {
                callback(0, null);
                return;
            }
            xhr.open(method, url, true);
            xhr.onreadystatechange = function () {
                if (xhr.readyState !== 4 || stopped) return;
                var data = null;
                try {
                    if (xhr.responseText) data = JSON.parse(xhr.responseText);
                } catch (parseErr) {
                    data = null;
                }
                callback(xhr.status, data);
            };
            xhr.send();
        }

        function startFastPoll() {
            onMode('fast-poll');
            function tick() {
                if (stopped) return;
                var url =
                    '/api/search/target?rev=' +
                    encodeURIComponent(String(huntRevision)) +
                    '&compact=1';
                xhrJson('GET', url, function (status, data) {
                    if (stopped) return;
                    if (status >= 200 && status < 300 && data) {
                        if (data.unchanged) return;
                        handlePayload(data);
                    }
                });
            }
            tick();
            fastPollTimer = setInterval(tick, opts.fastPollMs || 120);
        }

        function startLongPollXhr() {
            onMode('long-poll');
            function loop() {
                if (stopped) return;
                var url =
                    '/api/search/target?wait=1&rev=' +
                    encodeURIComponent(String(huntRevision)) +
                    '&timeout=20000';
                xhrJson('GET', url, function (status, data) {
                    if (stopped) return;
                    if (status >= 200 && status < 300 && data) {
                        handlePayload(data);
                    }
                    if (!stopped) loop();
                });
            }
            loop();
        }

        function startLongPollFetch() {
            onMode('long-poll');
            function loop() {
                if (stopped) return;
                var url =
                    '/api/search/target?wait=1&rev=' +
                    encodeURIComponent(String(huntRevision)) +
                    '&timeout=20000';
                if (typeof fetch === 'function') {
                    fetch(url)
                        .then(function (res) {
                            return res.json();
                        })
                        .then(function (data) {
                            if (!stopped) {
                                handlePayload(data);
                                loop();
                            }
                        })
                        .catch(function () {
                            if (!stopped) startFastPoll();
                        });
                } else {
                    startLongPollXhr();
                }
            }
            loop();
        }

        function startWebSocket() {
            var proto = global.location.protocol === 'https:' ? 'wss:' : 'ws:';
            var url = proto + '//' + global.location.host + '/api/hunt/ws';
            ws = new WebSocket(url);
            onMode('websocket');

            ws.onmessage = function (evt) {
                try {
                    handlePayload(JSON.parse(evt.data));
                } catch (e) {}
            };
            ws.onerror = function () {
                if (stopped) return;
                try {
                    ws.close();
                } catch (e2) {}
                ws = null;
                startLongPollFetch();
            };
            ws.onclose = function () {
                if (stopped) return;
                ws = null;
                startLongPollFetch();
            };
        }

        function startSse() {
            es = new EventSource('/api/hunt/stream');
            onMode('sse');
            es.onmessage = function (evt) {
                try {
                    handlePayload(JSON.parse(evt.data));
                } catch (e) {}
            };
            es.onerror = function () {
                if (stopped) return;
                try {
                    es.close();
                } catch (e2) {}
                es = null;
                startLongPollFetch();
            };
        }

        function start() {
            stopped = false;
            if (preferWebSocket && typeof WebSocket !== 'undefined') {
                try {
                    startWebSocket();
                    return;
                } catch (e) {}
            }
            if (typeof EventSource !== 'undefined' && opts.trySse !== false) {
                try {
                    startSse();
                    return;
                } catch (e2) {}
            }
            if (typeof fetch === 'function') {
                startLongPollFetch();
            } else {
                startLongPollXhr();
            }
        }

        return {
            start: start,
            stop: stopAll,
            getRevision: function () {
                return huntRevision;
            },
        };
    }

    global.MerlinHuntTransport = {
        create: createHuntTransport,
    };
})(typeof window !== 'undefined' ? window : global);
