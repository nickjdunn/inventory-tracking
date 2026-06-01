/**
 * Semicolon-separated category tag helpers for items.category column.
 */

const { sanitizeCategoryString } = require('./upc-lookup');

function splitCategoryTags(value) {
    if (value == null) return [];
    const raw = String(value).trim();
    if (!raw) return [];
    return raw
        .split(';')
        .map((t) => t.trim())
        .filter(Boolean);
}

function joinCategoryTags(tags) {
    const cleaned = (tags || []).map((t) => String(t).trim()).filter(Boolean);
    if (!cleaned.length) return null;
    return sanitizeCategoryString(cleaned.join(';'));
}

function dedupeTags(tags) {
    const seen = new Set();
    const out = [];
    (tags || []).forEach((tag) => {
        const t = String(tag).trim();
        if (!t || seen.has(t)) return;
        seen.add(t);
        out.push(t);
    });
    return out;
}

function renameTagInList(tags, fromTag, toTag) {
    const from = String(fromTag).trim();
    const to = String(toTag).trim();
    if (!from || !to) return dedupeTags(tags);
    return dedupeTags(
        (tags || []).map((t) => (t === from ? to : t))
    );
}

function deleteTagFromList(tags, tag) {
    const target = String(tag).trim();
    return (tags || []).filter((t) => t !== target);
}

function mergeTagInList(tags, fromTag, toTag) {
    const from = String(fromTag).trim();
    const to = String(toTag).trim();
    if (!from) return dedupeTags(tags);
    let next = deleteTagFromList(tags, from);
    if (to) {
        next = dedupeTags([...next, to]);
    }
    return next;
}

function transformCategoryField(value, transformFn) {
    if (value == null || String(value).trim() === '') return null;
    const next = transformFn(splitCategoryTags(value));
    return joinCategoryTags(next);
}

function collectUniqueCategories(rows) {
    const counts = new Map();
    (rows || []).forEach((row) => {
        splitCategoryTags(row.category).forEach((tag) => {
            counts.set(tag, (counts.get(tag) || 0) + 1);
        });
    });
    return [...counts.entries()]
        .map(([name, count]) => ({ name, count }))
        .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }));
}

module.exports = {
    splitCategoryTags,
    joinCategoryTags,
    dedupeTags,
    renameTagInList,
    deleteTagFromList,
    mergeTagInList,
    transformCategoryField,
    collectUniqueCategories,
};
