/**
 * Data enrichment wizard — Identify → Review/Filter → RFID link (shared desktop + mobile).
 */
(function (global) {
    const POLL_MS = 350;
    let pollTimer = null;
    let listenSince = 0;
    let capturedEpc = null;
    let audioCtx = null;
    let options = {};
    let wizardStep = 'identify';
    let staging = null;
    let boundUpc = null;
    let lastPickProducts = [];
    let categoryChipEditor = null;

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

    function ensureCategoryChipEditor() {
        if (categoryChipEditor) return categoryChipEditor;
        const host = getEl('onboard-review-category-chips');
        if (!host || !global.MerlinCategoryChips) return null;
        categoryChipEditor = global.MerlinCategoryChips.createCategoryChipEditor(host, {
            placeholder: 'Add tag…',
            hint: 'Press Enter or + to add each tag.',
            onChange: (tags) => {
                if (staging) staging.category = tags.join(';');
                const useCat = getEl('onboard-use-category');
                if (useCat) useCat.checked = tags.length > 0;
            },
        });
        return categoryChipEditor;
    }

    function categoryBadgesHtml(category) {
        if (global.MerlinInventory && global.MerlinInventory.renderCategoryBadgesHtml) {
            return global.MerlinInventory.renderCategoryBadgesHtml(category, escapeHtml, {
                empty: '—',
            });
        }
        if (!category) return '—';
        return escapeHtml(category);
    }

    function showStep(step) {
        wizardStep = step;
        const steps = ['identify', 'pick', 'review', 'rfid'];
        steps.forEach((name) => {
            const el = getEl('onboard-step-' + name);
            if (el) el.classList.toggle('hidden', name !== step);
        });
        const title = getEl('onboard-title');
        if (title) {
            if (step === 'identify') title.textContent = '➕ Add item — Step 1: Identify';
            else if (step === 'pick') title.textContent = '➕ Add item — Choose product';
            else if (step === 'review') title.textContent = '➕ Add item — Step 2: Keep / Edit / Review';
            else title.textContent = '➕ Add item — Step 3: Link RFID tag';
        }
    }

    function normalizeBoundUpc(upc, fallbackDigits) {
        if (upc && /^\d{8,14}$/.test(String(upc).replace(/\D/g, ''))) {
            return String(upc).replace(/\D/g, '');
        }
        return fallbackDigits || null;
    }

    function applyProductToStaging(product) {
        boundUpc = normalizeBoundUpc(product.upc, null);
        staging = {
            title: product.title || '',
            description: product.description || '',
            category: product.category || '',
            image_url: product.image_url || '',
            source: product.source || 'lookup',
        };
    }

    function thumbBlockHtml(imageUrl) {
        const url = imageUrl ? String(imageUrl).trim() : '';
        if (!url) {
            return '<span class="onboard-pick-thumb-fallback" aria-hidden="true">📦</span>';
        }
        return (
            '<span class="onboard-pick-thumb-wrap">' +
            '<img src="' +
            escapeHtml(url) +
            '" width="50" height="50" alt="" class="onboard-pick-thumb-img" referrerpolicy="no-referrer" ' +
            'onerror="this.style.display=\'none\';var n=this.nextElementSibling;if(n)n.style.display=\'flex\';">' +
            '<span class="onboard-pick-thumb-fallback" style="display:none" aria-hidden="true">📦</span>' +
            '</span>'
        );
    }

    function renderPickList(products) {
        const list = getEl('onboard-pick-list');
        if (!list) return;
        lastPickProducts = products || [];
        if (!lastPickProducts.length) {
            list.innerHTML = '<p class="onboard-pick-empty">No matches to display.</p>';
            return;
        }
        list.innerHTML = lastPickProducts
            .map((product, idx) => {
                const cat = categoryBadgesHtml(product.category);
                return (
                    '<button type="button" class="onboard-pick-row" data-pick-idx="' +
                    idx +
                    '">' +
                    thumbBlockHtml(product.image_url) +
                    '<span class="onboard-pick-text">' +
                    '<strong class="onboard-pick-title">' +
                    escapeHtml(product.title || 'Unknown') +
                    '</strong>' +
                    '<span class="onboard-pick-cat">' +
                    cat +
                    '</span>' +
                    '</span>' +
                    '</button>'
                );
            })
            .join('');

        list.querySelectorAll('.onboard-pick-row').forEach((btn) => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.getAttribute('data-pick-idx'), 10);
                selectPickProduct(lastPickProducts[idx]);
            });
        });
    }

    function selectPickProduct(product) {
        if (!product) return;
        applyProductToStaging(product);
        setLookupStatus('', false);
        populateReviewFields();
        showStep('review');
    }

    function emptyStaging() {
        return {
            title: '',
            description: '',
            category: '',
            image_url: '',
            source: null,
        };
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

    function setLookupStatus(msg, isErr) {
        const el = getEl('onboard-lookup-status');
        if (!el) return;
        el.textContent = msg || '';
        el.className = 'onboard-lookup-status' + (isErr ? ' err' : msg ? ' ok' : '');
    }

    function renderReviewImage() {
        const wrap = getEl('onboard-review-image-wrap');
        const useImg = getEl('onboard-use-image');
        if (!wrap) return;
        const url = staging && staging.image_url ? String(staging.image_url).trim() : '';
        if (url && useImg && useImg.checked) {
            wrap.innerHTML =
                '<img src="' +
                escapeHtml(url) +
                '" alt="Product" class="onboard-review-img" referrerpolicy="no-referrer">';
            wrap.classList.remove('hidden');
        } else {
            wrap.innerHTML = '<p class="onboard-no-image">No image selected</p>';
            wrap.classList.toggle('hidden', !url);
            if (!url) wrap.classList.remove('hidden');
        }
    }

    function populateReviewFields() {
        staging = staging || emptyStaging();
        getEl('onboard-review-title').value = staging.title || '';
        getEl('onboard-review-description').value = staging.description || '';
        const chips = ensureCategoryChipEditor();
        if (chips) chips.setFromString(staging.category || '');
        getEl('onboard-use-title').checked = true;
        getEl('onboard-use-description').checked = Boolean(staging.description);
        getEl('onboard-use-category').checked = Boolean(staging.category);
        getEl('onboard-use-image').checked = Boolean(staging.image_url);
        renderReviewImage();
        const src = getEl('onboard-review-source');
        if (src) {
            src.textContent = staging.source
                ? 'Source: ' + staging.source
                : 'Manual entry — check fields to import';
        }
    }

    function readReviewIntoStaging() {
        staging = staging || emptyStaging();
        staging.title = getEl('onboard-review-title').value.trim();
        staging.description = getEl('onboard-review-description').value.trim();
        const chips = ensureCategoryChipEditor();
        staging.category = chips ? chips.toSemicolonString() : '';
        if (!getEl('onboard-use-image').checked) {
            staging.image_url = '';
        }
    }

    function buildApprovedPayload() {
        readReviewIntoStaging();
        const useTitle = getEl('onboard-use-title').checked;
        const useDesc = getEl('onboard-use-description').checked;
        const useCat = getEl('onboard-use-category').checked;
        const useImg = getEl('onboard-use-image').checked;

        const name = useTitle ? staging.title : '';
        if (!name) return { error: 'Title is required — enable Title or enter a name' };

        return {
            name,
            description: useDesc ? staging.description || null : null,
            category: useCat ? staging.category || null : null,
            image_url: useImg && staging.image_url ? staging.image_url : null,
            upc: boundUpc || null,
        };
    }

    async function runProductLookup() {
        const raw = getEl('onboard-lookup-input').value.trim();
        if (!raw) {
            setLookupStatus('Enter a UPC barcode or product name to search', true);
            return;
        }

        const digits = raw.replace(/\D/g, '');
        const isUpc = digits.length >= 8 && digits.length <= 14 && digits.length === raw.replace(/[\s-]/g, '').length;

        const body = isUpc ? { upc: digits } : { text: raw };
        const btn = getEl('onboard-lookup-btn');
        if (btn) {
            btn.disabled = true;
            btn.textContent = 'Searching…';
        }
        setLookupStatus('Looking up product catalogs…', false);

        try {
            const res = await fetch('/api/lookup/product', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body),
            });
            const data = await res.json();

            if (!res.ok || !data.success) {
                setLookupStatus(data.message || data.error || 'No product found', true);
                return;
            }

            if (data.multiple && Array.isArray(data.products) && data.products.length > 1) {
                setLookupStatus(
                    data.products.length + ' matches — tap the correct product',
                    false
                );
                renderPickList(data.products);
                showStep('pick');
                return;
            }

            const single = data.multiple && data.products?.length === 1 ? data.products[0] : data;
            applyProductToStaging(single);
            if (!boundUpc && isUpc) boundUpc = digits;

            setLookupStatus('Found — review fields on the next screen', false);
            populateReviewFields();
            showStep('review');
        } catch (err) {
            setLookupStatus(err.message || 'Lookup failed', true);
        } finally {
            if (btn) {
                btn.disabled = false;
                btn.textContent = '🔍 Search / Lookup';
            }
        }
    }

    function goManualReview() {
        staging = emptyStaging();
        boundUpc = null;
        setLookupStatus('', false);
        populateReviewFields();
        showStep('review');
    }

    function backToIdentify() {
        stopPolling();
        lastPickProducts = [];
        showStep('identify');
    }

    function backFromPick() {
        lastPickProducts = [];
        showStep('identify');
    }

    function applyApprovedToRfidForm(approved) {
        getEl('onboard-name').value = approved.name;
        getEl('onboard-category').value = approved.category || '';
        getEl('onboard-description').value = approved.description || '';
        getEl('onboard-approved-summary').innerHTML =
            '<strong>' +
            escapeHtml(approved.name) +
            '</strong>' +
            (approved.category
                ? ' · ' + categoryBadgesHtml(approved.category)
                : '') +
            (approved.image_url
                ? '<br><span class="onboard-summary-img-note">Image URL saved with item</span>'
                : '');
    }

    async function beginRfidCapture() {
        const approved = buildApprovedPayload();
        if (approved.error) {
            alert(approved.error);
            return;
        }

        applyApprovedToRfidForm(approved);
        await loadContainersIntoSelect(getEl('onboard-home-bin'));
        showStep('rfid');
        startListening();
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
            'Captured at ' + rssi + ' dBm (ultra-near gate ≥ ' + nearGate + ' dBm)';
        getEl('onboard-register-btn').disabled = false;
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
        staging = emptyStaging();
        boundUpc = null;
        lastPickProducts = [];
        capturedEpc = null;
        getEl('onboard-lookup-input').value = '';
        setLookupStatus('', false);
        showStep('identify');

        const modal = getEl('onboard-modal');
        modal.classList.add('open');
        modal.setAttribute('aria-hidden', 'false');
        setTimeout(() => getEl('onboard-lookup-input').focus(), 120);
    }

    function closeWizard() {
        stopPolling();
        const modal = getEl('onboard-modal');
        modal.classList.remove('open');
        modal.setAttribute('aria-hidden', 'true');
        capturedEpc = null;
        staging = null;
        showStep('identify');
    }

    async function submitRegistration() {
        const approved = buildApprovedPayload();
        if (approved.error) {
            alert(approved.error);
            return;
        }

        const epc = getEl('onboard-epc').value.trim();
        if (!epc) {
            alert('No EPC captured — hold tag on antenna and pull trigger');
            return;
        }

        const payload = {
            epc_id: epc,
            name: approved.name,
            category: approved.category,
            description: approved.description,
            image_url: approved.image_url,
            upc: approved.upc,
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
            btn.textContent = '✓ Save item';
        }
    }

    function isOpen() {
        const modal = getEl('onboard-modal');
        return modal && modal.classList.contains('open');
    }

    function bindUi() {
        const openBtn = getEl('open-onboard-wizard');
        if (openBtn) openBtn.addEventListener('click', () => openWizard());

        const openAdd = getEl('open-add-item');
        if (openAdd) openAdd.addEventListener('click', () => openWizard());

        ['onboard-cancel', 'onboard-cancel-listen'].forEach((id) => {
            const el = getEl(id);
            if (el) el.addEventListener('click', closeWizard);
        });

        const modal = getEl('onboard-modal');
        if (modal) {
            modal.addEventListener('click', (e) => {
                if (e.target.id === 'onboard-modal') closeWizard();
            });
        }

        const regForm = getEl('onboard-register-form');
        if (regForm) {
            regForm.addEventListener('submit', (e) => {
                e.preventDefault();
                submitRegistration();
            });
        }

        const rescanBtn = getEl('onboard-rescan');
        if (rescanBtn) {
            rescanBtn.addEventListener('click', () => {
                unlockAudio();
                startListening();
            });
        }

        const lookupBtn = getEl('onboard-lookup-btn');
        if (lookupBtn) lookupBtn.addEventListener('click', () => runProductLookup());
        const skipBtn = getEl('onboard-skip-manual');
        if (skipBtn) skipBtn.addEventListener('click', () => goManualReview());
        const backBtn = getEl('onboard-review-back');
        if (backBtn) backBtn.addEventListener('click', () => backToIdentify());
        const pickBackBtn = getEl('onboard-pick-back');
        if (pickBackBtn) pickBackBtn.addEventListener('click', () => backFromPick());
        const contBtn = getEl('onboard-review-continue');
        if (contBtn) contBtn.addEventListener('click', () => beginRfidCapture());
        const useImg = getEl('onboard-use-image');
        if (useImg) useImg.addEventListener('change', renderReviewImage);
        const lookupInput = getEl('onboard-lookup-input');
        if (lookupInput) lookupInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                runProductLookup();
            }
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
