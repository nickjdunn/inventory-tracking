/**
 * Hybrid UPC lookup — tries providers in order; normalize to a shared product shape.
 * Results are cached in SQLite by the server after a successful hit.
 */

const FETCH_TIMEOUT_MS = 8000;

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

function normalizeUpc(raw) {
    const digits = String(raw ?? '').replace(/\D/g, '');
    if (digits.length < 8 || digits.length > 14) return null;
    return digits;
}

function buildResult(upc, source, fields) {
    const name = (fields.name || '').trim();
    if (!name) return null;
    return {
        found: true,
        upc,
        source,
        name,
        brand: fields.brand || null,
        category: fields.category || null,
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
        category: (p.categories || p.main_category || '').split(',')[0]?.trim(),
        description: p.ingredients_text || p.quantity,
        image_url: p.image_front_url || p.image_url,
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
        category: (p.categories || '').split(',')[0]?.trim(),
        description: p.quantity,
        image_url: p.image_front_url || p.image_url,
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
        category: (p.categories || '').split(',')[0]?.trim(),
        description: p.quantity,
        image_url: p.image_front_url || p.image_url,
    });
}

function mapUpcItemDbEntry(upc, item) {
    return buildResult(upc, 'upcitemdb', {
        name: item.title || item.description,
        brand: item.brand,
        category: item.category,
        description: item.description,
        image_url: item.images?.[0],
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

const TEXT_SEARCH_PAGE_SIZE = 10;

function mapOffSearchProduct(p, source, query) {
    const name = (p.product_name || p.generic_name || '').trim();
    if (!name) return null;
    const code = normalizeUpc(p.code || p._id);
    return buildResult(code || `text-${String(query).slice(0, 24)}`, source, {
        name,
        brand: p.brands,
        category: (p.categories || '').split(',')[0]?.trim(),
        description: p.quantity || p.generic_name,
        image_url: p.image_front_url || p.image_url,
    });
}

async function searchOpenFoodFactsProducts(query, pageSize) {
    const q = String(query ?? '').trim();
    if (q.length < 2) return [];

    const url =
        'https://world.openfoodfacts.org/cgi/search.pl?' +
        'search_simple=1&action=process&json=1&page_size=' +
        String(pageSize || TEXT_SEARCH_PAGE_SIZE) +
        '&fields=product_name,brands,categories,generic_name,quantity,image_front_url,image_url,code&search_terms=' +
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
        '&fields=product_name,brands,categories,generic_name,quantity,image_front_url,image_url,code&search_terms=' +
        encodeURIComponent(q);

    const data = await fetchJson(url);
    if (!data || !Array.isArray(data.products)) return [];

    return data.products
        .map((p) => mapOffSearchProduct(p, 'open_products_facts_search', q))
        .filter(Boolean);
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
 * Free-text product search (Open*Facts search APIs).
 */
async function lookupTextHybrid(rawQuery) {
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

    if (merged.length < 2) {
        providers_tried.push('open_products_facts_search');
        const more = await searchOpenProductsFactsProducts(query, TEXT_SEARCH_PAGE_SIZE);
        merged = dedupeProductResults(merged.concat(more));
    } else {
        merged = dedupeProductResults(merged);
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
        const result = await lookupTextHybrid(textRaw);
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
    normalizeUpc,
    PROVIDER_ORDER,
    TEXT_SEARCH_PAGE_SIZE,
};
