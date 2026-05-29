/**
 * Boundary tag near-field sniffer for bins.html
 */
(function (global) {
    const POLL_MS = 350;
    let pollTimer = null;
    let listenSince = 0;
    let activeInputId = null;
    let excludeBinId = null;
    let audioCtx = null;

    function getEl(id) {
        return document.getElementById(id);
    }

    /** Returns null when no bin exists yet (new-bin form). */
    function normalizeExcludeBinId(value) {
        if (value == null) return null;
        const trimmed = String(value).trim();
        if (!trimmed || trimmed.toLowerCase() === 'null' || trimmed.toLowerCase() === 'undefined') {
            return null;
        }
        return trimmed;
    }

    function resolveExcludeBinId(options) {
        if (options && options.excludeBinId !== undefined) {
            return normalizeExcludeBinId(options.excludeBinId);
        }
        const editing = normalizeExcludeBinId(getEl('editing-bin-id')?.value);
        if (editing) return editing;
        return normalizeExcludeBinId(getEl('bin-id')?.value);
    }

    async function unlockAudio() {
        if (!audioCtx) {
            const Ctx = global.AudioContext || global.webkitAudioContext;
            if (Ctx) audioCtx = new Ctx();
        }
        if (audioCtx && audioCtx.state === 'suspended') await audioCtx.resume();
    }

    function playCaptureTick() {
        if (!audioCtx || audioCtx.state !== 'running') return;
        const t0 = audioCtx.currentTime;
        [880, 1320].forEach((freq, i) => {
            const osc = audioCtx.createOscillator();
            const g = audioCtx.createGain();
            osc.type = 'sine';
            osc.frequency.setValueAtTime(freq, t0 + i * 0.07);
            g.gain.setValueAtTime(0.0001, t0 + i * 0.07);
            g.gain.exponentialRampToValueAtTime(0.11, t0 + i * 0.07 + 0.01);
            g.gain.exponentialRampToValueAtTime(0.0001, t0 + i * 0.07 + 0.07);
            osc.connect(g);
            g.connect(audioCtx.destination);
            osc.start(t0 + i * 0.07);
            osc.stop(t0 + i * 0.07 + 0.09);
        });
    }

    function flashInput(inputId) {
        const input = getEl(inputId);
        if (!input) return;
        input.classList.add('boundary-captured');
        setTimeout(() => input.classList.remove('boundary-captured'), 1800);
    }

    function clearPollTimer() {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
    }

    function stopPolling() {
        clearPollTimer();
        const modal = getEl('boundary-sniffer-modal');
        if (modal) {
            modal.classList.remove('open');
            modal.setAttribute('aria-hidden', 'true');
        }
        const radar = getEl('boundary-sniffer-radar');
        if (radar) radar.classList.remove('listening');
        activeInputId = null;
    }

    function setModalStatus(text) {
        const el = getEl('boundary-sniffer-status');
        if (el) el.textContent = text;
    }

    function siblingBoundaryInputId(activeInputId) {
        if (activeInputId === 'boundary-tag-a') return 'boundary-tag-b';
        if (activeInputId === 'boundary-tag-b') return 'boundary-tag-a';
        return null;
    }

    function normalizeEpc(value) {
        return String(value ?? '').trim().toLowerCase();
    }

    /** True when the other boundary field on this form already has the same EPC. */
    function isDuplicateOnSameBin(epc, activeInputId) {
        const siblingId = siblingBoundaryInputId(activeInputId);
        if (!siblingId) return false;
        const captured = normalizeEpc(epc);
        const sibling = normalizeEpc(getEl(siblingId)?.value);
        return captured !== '' && captured === sibling;
    }

    const DUPLICATE_SAME_BIN_MSG =
        'This tag is already set as the other boundary on this bin. Tag A and Tag B must be different EPCs.';

    async function pollOnce() {
        if (!activeInputId) return;

        try {
            let url =
                '/api/scan/latest-near-field?since=' +
                encodeURIComponent(listenSince) +
                '&purpose=boundary';
            if (excludeBinId) {
                url += '&exclude_bin_id=' + encodeURIComponent(excludeBinId);
            }

            const res = await fetch(url);
            const data = await res.json();
            if (!res.ok) return;

            if (data.rssi_near_gate != null) {
                const gateEl = getEl('boundary-sniffer-gate');
                if (gateEl) gateEl.textContent = data.rssi_near_gate;
            }

            if (data.captured && data.epc) {
                if (isDuplicateOnSameBin(data.epc, activeInputId)) {
                    global.alert(DUPLICATE_SAME_BIN_MSG);
                    setModalStatus(DUPLICATE_SAME_BIN_MSG);
                    return;
                }
                const input = getEl(activeInputId);
                if (input) input.value = data.epc;
                playCaptureTick();
                flashInput(activeInputId);
                setModalStatus('Captured ' + data.epc + ' @ ' + data.rssi + ' dBm');
                stopPolling();
                return;
            }

            if (data.reason === 'already_registered') {
                setModalStatus(
                    'Tag is already an item' +
                        (data.existing_name ? ' (“' + data.existing_name + '”)' : '') +
                        ' — use a blank boundary sticker'
                );
            } else if (data.reason === 'boundary_in_use') {
                setModalStatus('Tag already used on bin: ' + (data.container_name || data.container_id));
            } else if (data.reason === 'container_id') {
                setModalStatus(
                    'Tag matches bin ID' +
                        (data.container_name ? ' (“' + data.container_name + '”)' : '') +
                        ' — use a dedicated boundary sticker'
                );
            } else {
                setModalStatus('Listening… hold tag on antenna and pull trigger');
            }
        } catch (err) {
            setModalStatus('Poll failed: ' + err.message);
        }
    }

    async function startScan(inputId, options) {
        await unlockAudio();
        activeInputId = inputId;
        excludeBinId = resolveExcludeBinId(options || {});
        listenSince = Date.now();

        const modal = getEl('boundary-sniffer-modal');
        const radar = getEl('boundary-sniffer-radar');
        if (modal) {
            modal.classList.add('open');
            modal.setAttribute('aria-hidden', 'false');
        }
        if (radar) radar.classList.add('listening');
        setModalStatus('Listening… hold tag on antenna and pull trigger');

        clearPollTimer();
        pollOnce();
        pollTimer = setInterval(pollOnce, POLL_MS);
    }

    function init() {
        getEl('boundary-sniffer-cancel')?.addEventListener('click', stopPolling);
        getEl('boundary-sniffer-modal')?.addEventListener('click', (e) => {
            if (e.target.id === 'boundary-sniffer-modal') stopPolling();
        });
    }

    global.BoundarySniffer = {
        init,
        startScan,
        stop: stopPolling,
    };
})(typeof window !== 'undefined' ? window : global);
