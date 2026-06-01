/**
 * Hybrid UPC lookup — tries providers in order; normalize to a shared product shape.
 * Results are cached in SQLite by the server after a successful hit.
 */

const FETCH_TIMEOUT_MS = 10000;
const TEXT_SEARCH_PAGE_SIZE = 10;
const MAX_CATEGORY_TAGS = 6;

const PROVIDER_ORDER = [
    'open_food_facts',
    'open_products_facts',
    'open_beauty_facts',
    'upcitemdb',
];

async function fetchJson(url, headers = {}) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
    try {
        const res = await fetch(url, {
            headers: { Accept: 'application/json', ...headers },
            signal: controller.signal,
        });
        if (!res.ok) return null;
        return await res.json();
    } catch {
        return null;
    } finally {
        clearTimeout(timeout);
    }
}

async function fetchText(url, headers = {}) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
    try {
        const res = await fetch(url, {
            headers: {
                Accept: 'text/html,application/xhtml+xml',
                'User-Agent': 'RFID-Inventory-System/1.0 (+product-lookup)',
                ...headers,
            },
            signal: controller.signal,
        });
        if (!res.ok) return null;
        return await res.text();
    } catch {
        return null;
    } finally {
        clearTimeout(timeout);
    }
}

/**
 * Normalize multi-tag categories: "A ; B" → "A;B" (semicolon divider, no spaces).
 */
function sanitizeCategoryString(value) {
    if (value == null) return null;
    const raw = String(value).trim();
    if (!raw) return null;
    const parts = raw
        .split(/[;,]/)
        .map((p) => p.trim())
        .filter(Boolean);
    if (!parts.length) return null;
    return parts.join(';');
}

function normalizeUpc(raw) {
    const digits = String(raw ?? '').replace(/\D/g, '');
    if (digits.length < 8 || digits.length > 14) return null;
    return digits;
}

function pickBestImageUrl(candidates) {
    const urls = (Array.isArray(candidates) ? candidates : [candidates])
        .filter((u) => u && typeof u === 'string' && u.trim())
        .map((u) => u.trim());

    if (!urls.length) return null;

    const scored = urls.map((url) => {
        let score = 10;
        if (/small|thumb|icon|100\.|200\.|mini/i.test(url)) score -= 4;
        if (/front|full|large|display/i.test(url)) score += 3;
        if (/\.(jpg|jpeg|png|webp)(\?|$)/i.test(url)) score += 1;
        return { url, score };
    });

    scored.sort((a, b) => b.score - a.score || b.url.length - a.url.length);
    return scored[0].url;
}

function pickOffProductImage(p) {
    return pickBestImageUrl([
        p.image_front_url,
        p.image_url,
        p.selected_images?.front?.display?.en,
        p.selected_images?.front?.display,
        p.image_front_small_url,
    ]);
}

function extractOffCategories(p) {
    const tags = new Set();

    if (Array.isArray(p.categories_tags)) {
        p.categories_tags.forEach((tag) => {
            const t = String(tag)
                .replace(/^en:/, '')
                .replace(/-/g, ' ')
                .trim();
            if (t && t.length > 1 && t.length < 48) tags.add(t);
        });
    }

    const csv = p.categories || p.main_category || '';
    if (csv) {
        String(csv)
            .split(',')
            .forEach((part) => {
                const t = part.trim();
                if (t && t.length < 48) tags.add(t);
            });
    }

    return sanitizeCategoryString([...tags].slice(0, MAX_CATEGORY_TAGS).join(';'));
}

function extractUpcItemDbCategories(item) {
    return sanitizeCategoryString(item.category || item.category_path || null);
}

function pickUpcItemDbImage(item) {
    if (!item || !item.images) return null;
    const list = Array.isArray(item.images) ? item.images : [item.images];
    return pickBestImageUrl(list);
}

function buildOffDescription(p) {
    const parts = [
        p.generic_name,
        p.quantity,
        p.ingredients_text,
        p.packaging,
    ]
        .filter(Boolean)
        .map((s) => String(s).trim());
    const unique = [...new Set(parts)];
    const text = unique.join(' · ');
    return text.length > 500 ? text.slice(0, 497) + '…' : text || null;
}

function buildResult(upc, source, fields) {
    const name = (fields.name || '').trim();
    if (!name) return null;
    const category = sanitizeCategoryString(fields.category);
    return {
        found: true,
        upc,
        source,
        name,
        brand: fields.brand || null,
        category,
        description: fields.description || null,
        image_url: fields.image_url || null,
    };
}

async function lookupOpenFoodFacts(upc) {
    const data = await fetchJson(
        `https://world.openfoodfacts.org/api/v2/product/${upc}.json`
    );
    if (!data || data.status !== 1 || !data.product) return null;
    const p = data.product;
    return buildResult(upc, 'open_food_facts', {
        name: p.product_name || p.generic_name || p.abbreviated_product_name,
        brand: p.brands || p.brand_owner,
        category: extractOffCategories(p),
        description: buildOffDescription(p),
        image_url: pickOffProductImage(p),
    });
}

async function lookupOpenProductsFacts(upc) {
    const data = await fetchJson(
        `https://world.openproductsfacts.org/api/v2/product/${upc}.json`
    );
    if (!data || data.status !== 1 || !data.product) return null;
    const p = data.product;
    return buildResult(upc, 'open_products_facts', {
        name: p.product_name || p.generic_name,
        brand: p.brands,
        category: extractOffCategories(p),
        description: buildOffDescription(p),
        image_url: pickOffProductImage(p),
    });
}

async function lookupOpenBeautyFacts(upc) {
    const data = await fetchJson(
        `https://world.openbeautyfacts.org/api/v2/product/${upc}.json`
    );
    if (!data || data.status !== 1 || !data.product) return null;
    const p = data.product;
    return buildResult(upc, 'open_beauty_facts', {
        name: p.product_name || p.generic_name,
        brand: p.brands,
        category: extractOffCategories(p),
        description: buildOffDescription(p),
        image_url: pickOffProductImage(p),
    });
}

function mapUpcItemDbEntry(upc, item) {
    const code = normalizeUpc(item.upc || upc) || upc;
    return buildResult(code, 'upcitemdb', {
        name: item.title || item.description,
        brand: item.brand,
        category: extractUpcItemDbCategories(item),
        description: item.description || item.title,
        image_url: pickUpcItemDbImage(item),
    });
}

async function lookupUpcItemDb(upc, apiKey) {
    const items = await lookupUpcItemDbItems(upc, apiKey);
    return items.length ? items[0] : null;
}

async function lookupUpcItemDbItems(upc, apiKey) {
    if (!apiKey) return [];
    const data = await fetchJson(
        `https://api.upcitemdb.com/prod/trial/lookup?upc=${encodeURIComponent(upc)}`,
        { user_key: apiKey, key_type: '3scale' }
    );
    if (!data || data.code !== 'OK' || !data.items?.length) return [];
    return data.items.map((item) => mapUpcItemDbEntry(upc, item)).filter(Boolean);
}

/**
 * @param {string} rawUpc
 * @param {{ upcitemdbKey?: string }} options
 * @returns {Promise<{ found: boolean, upc: string, providers_tried: string[], ... }>}
 */
async function lookupUpcHybrid(rawUpc, options = {}) {
    const upc = normalizeUpc(rawUpc);
    const providers_tried = [];

    if (!upc) {
        return {
            found: false,
            upc: String(rawUpc ?? ''),
            error: 'Invalid UPC — use 8–14 digits',
            providers_tried,
        };
    }

    const runners = {
        open_food_facts: () => lookupOpenFoodFacts(upc),
        open_products_facts: () => lookupOpenProductsFacts(upc),
        open_beauty_facts: () => lookupOpenBeautyFacts(upc),
        upcitemdb: () => lookupUpcItemDb(upc, options.upcitemdbKey),
    };

    for (const id of PROVIDER_ORDER) {
        if (id === 'upcitemdb') {
            providers_tried.push(id);
            const items = await lookupUpcItemDbItems(upc, options.upcitemdbKey);
            if (items.length > 1) {
                return { found: true, multiple: true, products: items, providers_tried, upc };
            }
            if (items.length === 1) {
                return { ...items[0], providers_tried };
            }
            continue;
        }

        providers_tried.push(id);
        const result = await runners[id]();
        if (result) {
            return { ...result, providers_tried };
        }
    }

    return {
        found: false,
        upc,
        message: 'No product found in online catalogs. Enter details manually.',
        providers_tried,
    };
}

function mapOffSearchProduct(p, source, query) {
    const name = (p.product_name || p.generic_name || '').trim();
    if (!name) return null;
    const code = normalizeUpc(p.code || p._id);
    return buildResult(code || `text-${String(query).slice(0, 24)}`, source, {
        name,
        brand: p.brands,
        category: extractOffCategories(p),
        description: buildOffDescription(p),
        image_url: pickOffProductImage(p),
    });
}

async function searchOpenFoodFactsProducts(query, pageSize) {
    const q = String(query ?? '').trim();
    if (q.length < 2) return [];

    const url =
        'https://world.openfoodfacts.org/cgi/search.pl?' +
        'search_simple=1&action=process&json=1&page_size=' +
        String(pageSize || TEXT_SEARCH_PAGE_SIZE) +
        '&fields=product_name,brands,categories,categories_tags,generic_name,quantity,ingredients_text,packaging,image_front_url,image_url,image_front_small_url,code&search_terms=' +
        encodeURIComponent(q);

    const data = await fetchJson(url);
    if (!data || !Array.isArray(data.products)) return [];

    const qLower = q.toLowerCase();
    return data.products
        .map((p) => {
            const row = mapOffSearchProduct(p, 'open_food_facts_search', q);
            if (!row) return null;
            const boost = row.name.toLowerCase().includes(qLower) ? 1 : 0;
            return { row, boost };
        })
        .filter(Boolean)
        .sort((a, b) => b.boost - a.boost)
        .map((entry) => entry.row);
}

async function searchOpenProductsFactsProducts(query, pageSize) {
    const q = String(query ?? '').trim();
    if (q.length < 2) return [];

    const url =
        'https://world.openproductsfacts.org/cgi/search.pl?' +
        'search_simple=1&action=process&json=1&page_size=' +
        String(pageSize || TEXT_SEARCH_PAGE_SIZE) +
        '&fields=product_name,brands,categories,categories_tags,generic_name,quantity,ingredients_text,packaging,image_front_url,image_url,image_front_small_url,code&search_terms=' +
        encodeURIComponent(q);

    const data = await fetchJson(url);
    if (!data || !Array.isArray(data.products)) return [];

    return data.products
        .map((p) => mapOffSearchProduct(p, 'open_products_facts_search', q))
        .filter(Boolean);
}

async function searchUpcItemDbText(query, apiKey) {
    const q = String(query ?? '').trim();
    if (q.length < 2) return [];

    const headers = { Accept: 'application/json' };
    if (apiKey) {
        headers.user_key = apiKey;
        headers.key_type = '3scale';
    }

    const url =
        'https://api.upcitemdb.com/prod/trial/search?s=' + encodeURIComponent(q);
    const data = await fetchJson(url, headers);
    if (!data || data.code !== 'OK' || !Array.isArray(data.items)) return [];

    return data.items
        .map((item) => mapUpcItemDbEntry(item.upc || item.ean || '', item))
        .filter(Boolean);
}

/**
 * Lightweight HTML/OpenSearch fallback when catalog APIs return few hits.
 */
async function searchOpenSearchHints(query) {
    const q = String(query ?? '').trim();
    if (q.length < 2) return [];

    const url =
        'https://en.wikipedia.org/w/api.php?action=opensearch&search=' +
        encodeURIComponent(q) +
        '&limit=5&namespace=0&format=json';

    const data = await fetchJson(url);
    if (!data || !Array.isArray(data[1])) return [];

    const titles = data[1];
    const descriptions = data[2] || [];

    return titles
        .map((title, i) =>
            buildResult(`hint-${q.slice(0, 12)}-${i}`, 'wikipedia_opensearch', {
                name: title,
                brand: null,
                category: null,
                description: descriptions[i] || null,
                image_url: null,
            })
        )
        .filter(Boolean);
}

async function searchDuckDuckGoHtmlHints(query) {
    const q = String(query ?? '').trim();
    if (q.length < 2) return [];

    const html = await fetchText(
        'https://html.duckduckgo.com/html/?q=' + encodeURIComponent(q + ' product')
    );
    if (!html) return [];

    const results = [];
    const re = /<a[^>]+class="result__a"[^>]+href="([^"]+)"[^>]*>([\s\S]*?)<\/a>/gi;
    let match;
    while ((match = re.exec(html)) !== null && results.length < 5) {
        const title = match[2].replace(/<[^>]+>/g, '').trim();
        if (!title || title.length < 3) continue;
        results.push(
            buildResult(`ddg-${q.slice(0, 10)}-${results.length}`, 'duckduckgo_html', {
                name: title,
                brand: null,
                category: null,
                description: null,
                image_url: null,
            })
        );
    }
    return results.filter(Boolean);
}

function dedupeProductResults(products) {
    const seen = new Set();
    const out = [];
    products.forEach((item) => {
        if (!item || !item.name) return;
        const upcKey = item.upc ? String(item.upc).replace(/\D/g, '') : '';
        const key = upcKey && upcKey.length >= 8 ? 'u:' + upcKey : 'n:' + item.name.toLowerCase();
        if (seen.has(key)) return;
        seen.add(key);
        out.push(item);
    });
    return out;
}

/**
 * Free-text product search (Open*Facts → UPCitemdb → HTML/OpenSearch fallbacks).
 */
async function lookupTextHybrid(rawQuery, options = {}) {
    const query = String(rawQuery ?? '').trim();
    const providers_tried = [];

    if (query.length < 2) {
        return {
            found: false,
            query,
            error: 'Enter at least 2 characters to search',
            providers_tried,
        };
    }

    providers_tried.push('open_food_facts_search');
    let merged = await searchOpenFoodFactsProducts(query, TEXT_SEARCH_PAGE_SIZE);

    providers_tried.push('open_products_facts_search');
    const more = await searchOpenProductsFactsProducts(query, TEXT_SEARCH_PAGE_SIZE);
    merged = dedupeProductResults(merged.concat(more));

    if (merged.length < TEXT_SEARCH_PAGE_SIZE) {
        providers_tried.push('upcitemdb_search');
        const upcHits = await searchUpcItemDbText(query, options.upcitemdbKey);
        merged = dedupeProductResults(merged.concat(upcHits));
    }

    if (merged.length < 2) {
        providers_tried.push('wikipedia_opensearch');
        const wiki = await searchOpenSearchHints(query);
        merged = dedupeProductResults(merged.concat(wiki));
    }

    if (merged.length < 2) {
        providers_tried.push('duckduckgo_html');
        const ddg = await searchDuckDuckGoHtmlHints(query);
        merged = dedupeProductResults(merged.concat(ddg));
    }

    if (!merged.length) {
        return {
            found: false,
            query,
            message: 'No products matched that search. Try a UPC or enter details manually.',
            providers_tried,
        };
    }

    if (merged.length === 1) {
        return { ...merged[0], providers_tried, query, multiple: false };
    }

    return {
        found: true,
        multiple: true,
        products: merged,
        query,
        providers_tried,
    };
}

function internalResultToEnriched(result) {
    return {
        title: result.name || '',
        description: result.description || null,
        category: result.category || null,
        image_url: result.image_url || null,
        upc: result.upc || null,
        brand: result.brand || null,
        source: result.source || null,
    };
}

function toEnrichedProduct(result) {
    if (!result || !result.found) {
        return {
            success: false,
            title: null,
            description: null,
            category: null,
            image_url: null,
            upc: result?.upc || null,
            message: result?.message || result?.error || 'Lookup failed',
            providers_tried: result?.providers_tried || [],
        };
    }

    if (result.multiple && Array.isArray(result.products) && result.products.length > 0) {
        return {
            success: true,
            multiple: true,
            products: result.products.map(internalResultToEnriched),
            query: result.query || null,
            providers_tried: result.providers_tried || [],
        };
    }

    return {
        success: true,
        multiple: false,
        ...internalResultToEnriched(result),
        providers_tried: result.providers_tried || [],
    };
}

/**
 * Universal lookup — UPC barcode or free-text query.
 * @param {{ upc?: string, text?: string }} input
 */
async function lookupProduct(input, options = {}) {
    const upcRaw = input?.upc != null ? String(input.upc).trim() : '';
    const textRaw = input?.text != null ? String(input.text).trim() : '';

    if (upcRaw && textRaw) {
        return {
            success: false,
            error: 'Send either upc or text, not both',
        };
    }

    if (upcRaw) {
        const result = await lookupUpcHybrid(upcRaw, options);
        return toEnrichedProduct(result);
    }

    if (textRaw) {
        const result = await lookupTextHybrid(textRaw, options);
        return toEnrichedProduct(result);
    }

    return {
        success: false,
        error: 'Provide { upc } or { text } in the request body',
    };
}

module.exports = {
    lookupUpcHybrid,
    lookupTextHybrid,
    lookupProduct,
    toEnrichedProduct,
    internalResultToEnriched,
    dedupeProductResults,
    sanitizeCategoryString,
    normalizeUpc,
    PROVIDER_ORDER,
    TEXT_SEARCH_PAGE_SIZE,
};
