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

module.exports = {
    lookupUpcHybrid,
    normalizeUpc,
    PROVIDER_ORDER,
};
