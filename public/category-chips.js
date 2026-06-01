/**
 * Interactive category tag / chip editor (desktop + mobile).
 * Serializes to semicolon-separated string for items.category.
 */
(function (global) {
    const registry = new Map();
    let masterCategoryCache = null;
    let masterCategoryPromise = null;

    function splitTags(str) {
        if (global.MerlinInventory && global.MerlinInventory.splitCategoryTags) {
            return global.MerlinInventory.splitCategoryTags(str);
        }
        if (str == null) return [];
        return String(str)
            .split(';')
            .map((t) => t.trim())
            .filter(Boolean);
    }

    async function fetchMasterCategoryNames() {
        if (masterCategoryCache) return masterCategoryCache.slice();
        if (masterCategoryPromise) return masterCategoryPromise;

        masterCategoryPromise = fetch('/api/categories')
            .then((res) => res.json())
            .then((data) => {
                const rows = Array.isArray(data.categories) ? data.categories : [];
                masterCategoryCache = rows
                    .map((row) => String(row.name || '').trim())
                    .filter(Boolean);
                return masterCategoryCache.slice();
            })
            .catch(() => {
                masterCategoryCache = [];
                return [];
            })
            .finally(() => {
                masterCategoryPromise = null;
            });

        return masterCategoryPromise;
    }

    function invalidateMasterCategoryCache() {
        masterCategoryCache = null;
    }

    function createCategoryChipEditor(containerEl, options) {
        if (!containerEl) return null;
        options = options || {};

        const tags = [];
        containerEl.innerHTML = '';
        containerEl.classList.add('category-chip-host');

        const editor = document.createElement('div');
        editor.className = 'category-chip-editor';

        const chipsRow = document.createElement('div');
        chipsRow.className = 'category-chips-row';
        chipsRow.setAttribute('aria-live', 'polite');

        const addWrap = document.createElement('div');
        addWrap.className = 'category-chip-add-wrap';

        const addRow = document.createElement('div');
        addRow.className = 'category-chip-add-row';

        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'category-chip-input';
        input.placeholder = options.placeholder || 'Add category tag…';
        input.setAttribute('aria-label', options.inputLabel || 'New category tag');
        input.setAttribute('autocomplete', 'off');
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-autocomplete', 'list');

        const addBtn = document.createElement('button');
        addBtn.type = 'button';
        addBtn.className = 'category-chip-add-btn';
        addBtn.textContent = '+';
        addBtn.title = 'Add tag';

        const typeahead = document.createElement('ul');
        typeahead.className = 'category-typeahead';
        typeahead.setAttribute('role', 'listbox');
        typeahead.hidden = true;

        let filteredSuggestions = [];
        let highlightIndex = -1;

        function notifyChange() {
            if (typeof options.onChange === 'function') {
                options.onChange(tags.slice());
            }
        }

        function renderChips() {
            chipsRow.innerHTML = '';
            tags.forEach((tag, idx) => {
                const chip = document.createElement('span');
                chip.className = 'category-chip';

                const label = document.createElement('span');
                label.className = 'category-chip-label';
                label.textContent = tag;

                const removeBtn = document.createElement('button');
                removeBtn.type = 'button';
                removeBtn.className = 'category-chip-remove';
                removeBtn.textContent = '×';
                removeBtn.setAttribute('aria-label', 'Remove ' + tag);
                removeBtn.addEventListener('click', () => {
                    tags.splice(idx, 1);
                    renderChips();
                    notifyChange();
                    updateTypeahead();
                });

                chip.appendChild(label);
                chip.appendChild(removeBtn);
                chipsRow.appendChild(chip);
            });
        }

        function hideTypeahead() {
            typeahead.hidden = true;
            typeahead.innerHTML = '';
            highlightIndex = -1;
            filteredSuggestions = [];
            input.setAttribute('aria-expanded', 'false');
        }

        function acceptSuggestion(name) {
            const value = String(name ?? '').trim();
            if (!value) return;
            addTag(value);
            hideTypeahead();
            input.focus();
        }

        function renderTypeaheadList() {
            typeahead.innerHTML = '';
            filteredSuggestions.forEach((name, idx) => {
                const li = document.createElement('li');
                li.className = 'category-typeahead-item' + (idx === highlightIndex ? ' active' : '');
                li.setAttribute('role', 'option');
                li.textContent = name;
                li.addEventListener('mousedown', (e) => {
                    e.preventDefault();
                    acceptSuggestion(name);
                });
                typeahead.appendChild(li);
            });
            typeahead.hidden = filteredSuggestions.length === 0;
            input.setAttribute('aria-expanded', filteredSuggestions.length ? 'true' : 'false');
        }

        function filterSuggestions(query, masterList) {
            const q = String(query ?? '')
                .trim()
                .toLowerCase();
            const existing = new Set(tags.map((t) => t.toLowerCase()));
            const list = masterList || [];
            const matches = list.filter((name) => {
                if (existing.has(name.toLowerCase())) return false;
                if (!q) return true;
                return name.toLowerCase().includes(q);
            });
            return matches.slice(0, 10);
        }

        async function updateTypeahead() {
            const master = await fetchMasterCategoryNames();
            const query = input.value;
            filteredSuggestions = filterSuggestions(query, master);
            if (highlightIndex >= filteredSuggestions.length) {
                highlightIndex = filteredSuggestions.length - 1;
            }
            if (highlightIndex < 0 && filteredSuggestions.length) {
                highlightIndex = 0;
            }
            renderTypeaheadList();
        }

        function addTag(raw) {
            const value = String(raw ?? '').trim();
            if (!value) return false;
            if (tags.some((t) => t.toLowerCase() === value.toLowerCase())) {
                input.value = '';
                hideTypeahead();
                return false;
            }
            tags.push(value);
            input.value = '';
            renderChips();
            notifyChange();
            hideTypeahead();
            return true;
        }

        function acceptHighlightedOrTyped() {
            if (highlightIndex >= 0 && filteredSuggestions[highlightIndex]) {
                acceptSuggestion(filteredSuggestions[highlightIndex]);
                return true;
            }
            return addTag(input.value);
        }

        addBtn.addEventListener('click', () => acceptHighlightedOrTyped());

        input.addEventListener('focus', () => {
            fetchMasterCategoryNames().then(() => updateTypeahead());
        });

        input.addEventListener('input', () => {
            highlightIndex = -1;
            updateTypeahead();
        });

        input.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowDown') {
                if (!filteredSuggestions.length) return;
                e.preventDefault();
                highlightIndex = Math.min(
                    filteredSuggestions.length - 1,
                    highlightIndex + 1
                );
                renderTypeaheadList();
                return;
            }
            if (e.key === 'ArrowUp') {
                if (!filteredSuggestions.length) return;
                e.preventDefault();
                highlightIndex = Math.max(0, highlightIndex - 1);
                renderTypeaheadList();
                return;
            }
            if (e.key === 'Escape') {
                hideTypeahead();
                return;
            }
            if (e.key === 'Tab' && highlightIndex >= 0 && filteredSuggestions[highlightIndex]) {
                e.preventDefault();
                acceptSuggestion(filteredSuggestions[highlightIndex]);
                return;
            }
            if (e.key === 'Enter') {
                e.preventDefault();
                acceptHighlightedOrTyped();
            }
        });

        document.addEventListener('click', (e) => {
            if (!addWrap.contains(e.target)) hideTypeahead();
        });

        addRow.appendChild(input);
        addRow.appendChild(addBtn);
        addWrap.appendChild(addRow);
        addWrap.appendChild(typeahead);
        editor.appendChild(chipsRow);
        editor.appendChild(addWrap);
        containerEl.appendChild(editor);

        if (options.hint) {
            const hint = document.createElement('p');
            hint.className = 'category-chip-hint';
            hint.textContent = options.hint;
            containerEl.appendChild(hint);
        }

        const api = {
            setFromString(str) {
                tags.length = 0;
                splitTags(str).forEach((t) => tags.push(t));
                renderChips();
            },
            getTags() {
                return tags.slice();
            },
            toSemicolonString() {
                return tags.join(';');
            },
            clear() {
                tags.length = 0;
                renderChips();
                notifyChange();
            },
            focusInput() {
                input.focus();
            },
            refreshSuggestions() {
                return updateTypeahead();
            },
        };

        if (options.initialValue) {
            api.setFromString(options.initialValue);
        }

        return api;
    }

    function mountById(hostId, options) {
        const el = document.getElementById(hostId);
        if (!el) return null;
        const editor = createCategoryChipEditor(el, options);
        if (editor) registry.set(hostId, editor);
        return editor;
    }

    function getEditor(hostId) {
        return registry.get(hostId) || null;
    }

    global.MerlinCategoryChips = {
        createCategoryChipEditor,
        mountById,
        getEditor,
        fetchMasterCategoryNames,
        invalidateMasterCategoryCache,
    };
})(typeof window !== 'undefined' ? window : global);
