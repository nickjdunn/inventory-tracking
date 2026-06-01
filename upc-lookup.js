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

async function lookupUpcItemDb(upc, apiKey) {
    if (!apiKey) return null;
    const data = await fetchJson(
        `https://api.upcitemdb.com/prod/trial/lookup?upc=${encodeURIComponent(upc)}`,
        { user_key: apiKey, key_type: '3scale' }
    );
    if (!data || data.code !== 'OK' || !data.items?.length) return null;
    const item = data.items[0];
    return buildResult(upc, 'upcitemdb', {
        name: item.title || item.description,
        brand: item.brand,
        category: item.category,
        description: item.description,
        image_url: item.images?.[0],
    });
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

async function lookupOpenFoodFactsText(query) {
    const q = String(query ?? '').trim();
    if (q.length < 2) return null;

    const url =
        'https://world.openfoodfacts.org/cgi/search.pl?' +
        'search_simple=1&action=process&json=1&page_size=5&fields=product_name,brands,categories,' +
        'generic_name,quantity,image_front_url,image_url,code&search_terms=' +
        encodeURIComponent(q);

    const data = await fetchJson(url);
    if (!data || !Array.isArray(data.products) || !data.products.length) return null;

    const ranked = data.products
        .map((p) => {
            const name = (p.product_name || p.generic_name || '').trim();
            if (!name) return null;
            const score =
                name.toLowerCase().includes(q.toLowerCase()) ? 2 : 1;
            return { p, name, score };
        })
        .filter(Boolean)
        .sort((a, b) => b.score - a.score);

    if (!ranked.length) return null;
    const p = ranked[0].p;
    const upc = normalizeUpc(p.code || p._id) || null;

    return buildResult(upc || `text-${q.slice(0, 24)}`, 'open_food_facts_search', {
        name: ranked[0].name,
        brand: p.brands,
        category: (p.categories || '').split(',')[0]?.trim(),
        description: p.quantity || p.generic_name,
        image_url: p.image_front_url || p.image_url,
    });
}

async function lookupOpenProductsFactsText(query) {
    const q = String(query ?? '').trim();
    if (q.length < 2) return null;

    const url =
        'https://world.openproductsfacts.org/cgi/search.pl?' +
        'search_simple=1&action=process&json=1&page_size=5&fields=product_name,brands,categories,' +
        'generic_name,quantity,image_front_url,image_url,code&search_terms=' +
        encodeURIComponent(q);

    const data = await fetchJson(url);
    if (!data || !Array.isArray(data.products) || !data.products.length) return null;

    const p = data.products[0];
    const name = (p.product_name || p.generic_name || '').trim();
    if (!name) return null;
    const upc = normalizeUpc(p.code || p._id) || null;

    return buildResult(upc || `text-${q.slice(0, 24)}`, 'open_products_facts_search', {
        name,
        brand: p.brands,
        category: (p.categories || '').split(',')[0]?.trim(),
        description: p.quantity,
        image_url: p.image_front_url || p.image_url,
    });
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
    let result = await lookupOpenFoodFactsText(query);
    if (result) {
        return { ...result, providers_tried, query };
    }

    providers_tried.push('open_products_facts_search');
    result = await lookupOpenProductsFactsText(query);
    if (result) {
        return { ...result, providers_tried, query };
    }

    return {
        found: false,
        query,
        message: 'No products matched that search. Try a UPC or enter details manually.',
        providers_tried,
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

    return {
        success: true,
        title: result.name || '',
        description: result.description || null,
        category: result.category || null,
        image_url: result.image_url || null,
        upc: result.upc || null,
        brand: result.brand || null,
        source: result.source || null,
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
    normalizeUpc,
    PROVIDER_ORDER,
};
