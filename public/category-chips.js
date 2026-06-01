/**
 * Interactive category tag / chip editor (desktop + mobile).
 * Serializes to semicolon-separated string for items.category.
 */
(function (global) {
    const registry = new Map();

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

        const addRow = document.createElement('div');
        addRow.className = 'category-chip-add-row';

        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'category-chip-input';
        input.placeholder = options.placeholder || 'Add category tag…';
        input.setAttribute('aria-label', options.inputLabel || 'New category tag');

        const addBtn = document.createElement('button');
        addBtn.type = 'button';
        addBtn.className = 'category-chip-add-btn';
        addBtn.textContent = '+';
        addBtn.title = 'Add tag';

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
                });

                chip.appendChild(label);
                chip.appendChild(removeBtn);
                chipsRow.appendChild(chip);
            });
        }

        function addTag(raw) {
            const value = String(raw ?? '').trim();
            if (!value) return false;
            if (tags.some((t) => t.toLowerCase() === value.toLowerCase())) {
                input.value = '';
                return false;
            }
            tags.push(value);
            input.value = '';
            renderChips();
            notifyChange();
            return true;
        }

        addBtn.addEventListener('click', () => addTag(input.value));
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                addTag(input.value);
            }
        });

        addRow.appendChild(input);
        addRow.appendChild(addBtn);
        editor.appendChild(chipsRow);
        editor.appendChild(addRow);
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
    };
})(typeof window !== 'undefined' ? window : global);
