/**
 * High-RSSI onboarding wizard — shared by desktop and mobile.
 */
(function (global) {
    const POLL_MS = 350;
    let pollTimer = null;
    let listenSince = 0;
    let capturedEpc = null;
    let audioCtx = null;
    let options = {};

    function getEl(id) {
        return document.getElementById(id);
    }

    function escapeHtml(str) {
        return String(str ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    async function unlockAudio() {
        if (!audioCtx) {
            const Ctx = global.AudioContext || global.webkitAudioContext;
            if (Ctx) audioCtx = new Ctx();
        }
        if (audioCtx && audioCtx.state === 'suspended') await audioCtx.resume();
    }

    function playTone(freq, duration, type, gain, delay) {
        if (!audioCtx || audioCtx.state !== 'running') return;
        const t0 = audioCtx.currentTime + (delay || 0);
        const osc = audioCtx.createOscillator();
        const g = audioCtx.createGain();
        osc.type = type || 'sine';
        osc.frequency.setValueAtTime(freq, t0);
        g.gain.setValueAtTime(0.0001, t0);
        g.gain.exponentialRampToValueAtTime(gain, t0 + 0.01);
        g.gain.exponentialRampToValueAtTime(0.0001, t0 + duration);
        osc.connect(g);
        g.connect(audioCtx.destination);
        osc.start(t0);
        osc.stop(t0 + duration + 0.02);
    }

    function playCaptureTick() {
        playTone(880, 0.06, 'sine', 0.12);
        playTone(1320, 0.05, 'sine', 0.1, 0.07);
    }

    function playConfirmChime() {
        [523.25, 659.25, 783.99].forEach((freq, i) => {
            playTone(freq, 0.16, 'sine', 0.14, i * 0.1);
        });
    }

    function stopPolling() {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
    }

    async function loadContainersIntoSelect(selectEl) {
        const res = await fetch('/api/containers');
        const containers = res.ok ? await res.json() : [];
        selectEl.innerHTML =
            '<option value="">— No home bin —</option>' +
            containers
                .map(
                    (c) =>
                        `<option value="${escapeHtml(c.id)}">${escapeHtml(c.name)} (${escapeHtml(c.id)})</option>`
                )
                .join('');
    }

    function showListenMode() {
        capturedEpc = null;
        getEl('onboard-listen-zone').classList.remove('hidden');
        getEl('onboard-form-zone').classList.add('hidden');
        const listenActions = getEl('onboard-listen-actions');
        if (listenActions) listenActions.classList.remove('hidden');
        const radar = getEl('onboard-radar');
        radar.classList.remove('captured');
        radar.classList.add('listening');
        getEl('onboard-listen-text').textContent =
            'Hold a new tag against the antenna and pull the trigger…';
        getEl('onboard-register-btn').disabled = true;
    }

    function showFormMode(epc, rssi, nearGate) {
        capturedEpc = epc;
        getEl('onboard-listen-zone').classList.add('hidden');
        getEl('onboard-form-zone').classList.remove('hidden');
        const listenActions = getEl('onboard-listen-actions');
        if (listenActions) listenActions.classList.add('hidden');
        const radar = getEl('onboard-radar');
        radar.classList.remove('listening');
        radar.classList.add('captured');
        getEl('onboard-epc').value = epc;
        getEl('onboard-capture-meta').textContent =
            `Captured at ${rssi} dBm (ultra-near gate ≥ ${nearGate} dBm)`;
        getEl('onboard-name').value = '';
        getEl('onboard-category').value = '';
        getEl('onboard-description').value = '';
        getEl('onboard-register-btn').disabled = false;
        setTimeout(() => getEl('onboard-name').focus(), 120);
    }

    async function pollNearField() {
        try {
            const res = await fetch(
                '/api/scan/latest-near-field?since=' + encodeURIComponent(listenSince)
            );
            const data = await res.json();
            if (!res.ok) return;

            if (data.rssi_near_gate != null) {
                getEl('onboard-gate-display').textContent = data.rssi_near_gate;
            }

            if (data.captured && data.epc) {
                stopPolling();
                playCaptureTick();
                await loadContainersIntoSelect(getEl('onboard-home-bin'));
                showFormMode(data.epc, data.rssi, data.rssi_near_gate);
                return;
            }

            if (data.reason === 'already_registered') {
                getEl('onboard-listen-text').textContent =
                    'Tag is already an item' +
                    (data.existing_name ? ' (“' + data.existing_name + '”)' : '') +
                    ' — use a new sticker';
            } else if (data.reason === 'boundary_in_use') {
                getEl('onboard-listen-text').textContent =
                    'Tag is a boundary on bin ' + (data.container_name || data.container_id);
            } else if (data.reason === 'container_id') {
                getEl('onboard-listen-text').textContent =
                    'Tag matches a bin ID — cannot register as an item';
            }
        } catch (err) {
            console.warn('Near-field poll failed:', err);
        }
    }

    function startListening() {
        listenSince = Date.now();
        showListenMode();
        stopPolling();
        pollNearField();
        pollTimer = setInterval(pollNearField, POLL_MS);
    }

    async function openWizard() {
        await unlockAudio();
        if (typeof options.onBeforeOpen === 'function') {
            await options.onBeforeOpen();
        }
        const modal = getEl('onboard-modal');
        modal.classList.add('open');
        modal.setAttribute('aria-hidden', 'false');
        startListening();
    }

    function closeWizard() {
        stopPolling();
        const modal = getEl('onboard-modal');
        modal.classList.remove('open');
        modal.setAttribute('aria-hidden', 'true');
        capturedEpc = null;
    }

    async function submitRegistration() {
        const name = getEl('onboard-name').value.trim();
        const epc = getEl('onboard-epc').value.trim();
        if (!name) {
            alert('Item name is required');
            return;
        }
        if (!epc) {
            alert('No EPC captured — reopen the wizard and scan again');
            return;
        }

        const payload = {
            epc_id: epc,
            name,
            category: getEl('onboard-category').value.trim() || null,
            description: getEl('onboard-description').value.trim() || null,
            home_container_id: getEl('onboard-home-bin').value || null,
        };

        const btn = getEl('onboard-register-btn');
        btn.disabled = true;
        btn.textContent = 'Saving…';

        try {
            const res = await fetch('/api/items', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload),
            });
            const data = await res.json();
            if (!res.ok) throw new Error(data.error || 'Registration failed');

            playConfirmChime();
            closeWizard();
            if (typeof options.onRegistered === 'function') {
                await options.onRegistered(data);
            }
        } catch (err) {
            alert(err.message);
        } finally {
            btn.disabled = false;
            btn.textContent = '✓ Register asset';
        }
    }

    function isOpen() {
        const modal = getEl('onboard-modal');
        return modal && modal.classList.contains('open');
    }

    function bindUi() {
        getEl('open-onboard-wizard').addEventListener('click', () => openWizard());
        ['onboard-cancel', 'onboard-cancel-listen'].forEach((id) => {
            const el = getEl(id);
            if (el) el.addEventListener('click', closeWizard);
        });
        getEl('onboard-modal').addEventListener('click', (e) => {
            if (e.target.id === 'onboard-modal') closeWizard();
        });
        getEl('onboard-register-form').addEventListener('submit', (e) => {
            e.preventDefault();
            submitRegistration();
        });
        getEl('onboard-rescan').addEventListener('click', () => {
            unlockAudio();
            startListening();
        });
    }

    function init(userOptions) {
        options = userOptions || {};
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', bindUi);
        } else {
            bindUi();
        }
    }

    global.MerlinOnboarding = {
        init,
        openWizard,
        closeWizard,
        isOpen,
        ingestNearFieldTags: async (tags) => {
            const res = await fetch('/api/scan/near-field-ingest', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ scanned_tags: tags }),
            });
            return res.json();
        },
    };
})(typeof window !== 'undefined' ? window : global);
