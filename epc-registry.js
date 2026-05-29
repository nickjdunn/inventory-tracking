/**
 * Global EPC registry — each EPC may exist in only one role:
 * inventory item, bin ID, or boundary tag (on exactly one bin).
 */
const db = require('./database');

function normalizeEpc(value) {
    const trimmed = value == null ? '' : String(value).trim();
    return trimmed === '' ? null : trimmed;
}

function normalizeExcludeId(value) {
    if (value == null) return null;
    const trimmed = String(value).trim();
    if (!trimmed || trimmed.toLowerCase() === 'null' || trimmed.toLowerCase() === 'undefined') {
        return null;
    }
    return trimmed;
}

function epcEquals(a, b) {
    const left = normalizeEpc(a);
    const right = normalizeEpc(b);
    if (!left || !right) return false;
    return left.toLowerCase() === right.toLowerCase();
}

function boundaryTagsAreDuplicate(boundaryA, boundaryB) {
    if (!boundaryA || !boundaryB) return false;
    return boundaryA.toLowerCase() === boundaryB.toLowerCase();
}

function normalizeBoundaryTag(value) {
    return normalizeEpc(value);
}

/**
 * @param {string} epc
 * @param {{ role: 'item'|'boundary'|'container_id', excludeBinId?: string|null, excludeItemEpc?: string|null }} options
 * @param {(err: Error|null, result: object) => void} callback
 */
function checkEpcForRole(epc, options, callback) {
    const normalized = normalizeEpc(epc);
    const role = options?.role || 'item';
    const excludeBinId = normalizeExcludeId(options?.excludeBinId);
    const excludeItemEpc = normalizeEpc(options?.excludeItemEpc);

    if (!normalized) {
        return callback(null, { valid: false, reason: 'empty_epc', message: 'EPC is required' });
    }

    db.get(
        `SELECT epc_id, name FROM items WHERE LOWER(epc_id) = LOWER(?)`,
        [normalized],
        (itemErr, itemRow) => {
            if (itemErr) return callback(itemErr);

            if (itemRow && !epcEquals(itemRow.epc_id, excludeItemEpc)) {
                return callback(null, {
                    valid: false,
                    reason: 'registered_item',
                    message: `EPC is already registered as item "${itemRow.name}"`,
                    epc: normalized,
                    item_name: itemRow.name,
                    item_epc: itemRow.epc_id,
                });
            }

            const onContainerIdRow = (containerErr, containerRow) => {
                if (containerErr) return callback(containerErr);

                const isOwnBinId =
                    role === 'container_id' && excludeBinId && epcEquals(containerRow?.id, excludeBinId);

                if (containerRow && !isOwnBinId) {
                    return callback(null, {
                        valid: false,
                        reason: 'container_id',
                        message: `EPC matches bin ID "${containerRow.name}" (${containerRow.id})`,
                        epc: normalized,
                        container_id: containerRow.id,
                        container_name: containerRow.name,
                    });
                }

                const onBoundaryRow = (boundErr, boundaryRow) => {
                    if (boundErr) return callback(boundErr);

                    if (boundaryRow) {
                        return callback(null, {
                            valid: false,
                            reason: 'boundary_in_use',
                            message: `EPC is already a boundary tag on bin "${boundaryRow.name}" (${boundaryRow.id})`,
                            epc: normalized,
                            container_id: boundaryRow.id,
                            container_name: boundaryRow.name,
                        });
                    }

                    callback(null, { valid: true, epc: normalized });
                };

                if (excludeBinId) {
                    db.get(
                        `SELECT id, name FROM containers
                         WHERE (LOWER(boundary_tag_a) = LOWER(?) OR LOWER(boundary_tag_b) = LOWER(?))
                           AND LOWER(id) != LOWER(?)`,
                        [normalized, normalized, excludeBinId],
                        onBoundaryRow
                    );
                } else {
                    db.get(
                        `SELECT id, name FROM containers
                         WHERE LOWER(boundary_tag_a) = LOWER(?) OR LOWER(boundary_tag_b) = LOWER(?)`,
                        [normalized, normalized],
                        onBoundaryRow
                    );
                }
            };

            db.get(`SELECT id, name FROM containers WHERE LOWER(id) = LOWER(?)`, [normalized], onContainerIdRow);
        }
    );
}

function checkEpcForRoleAsync(epc, options) {
    return new Promise((resolve, reject) => {
        checkEpcForRole(epc, options, (err, result) => {
            if (err) reject(err);
            else resolve(result);
        });
    });
}

/**
 * Validates bin create/update: distinct boundaries + registry for bin id and each tag.
 */
function validateContainerSave({ binId, boundaryA, boundaryB, excludeBinId }, callback) {
    const tagA = normalizeBoundaryTag(boundaryA);
    const tagB = normalizeBoundaryTag(boundaryB);
    const bin = normalizeEpc(binId);
    const exclude = normalizeExcludeId(excludeBinId);

    if (boundaryTagsAreDuplicate(tagA, tagB)) {
        return callback(null, {
            ok: false,
            reason: 'duplicate_boundary_same_bin',
            error: 'Boundary Tag A and Tag B must be different EPCs',
        });
    }

    if (bin && tagA && epcEquals(bin, tagA)) {
        return callback(null, {
            ok: false,
            reason: 'boundary_matches_bin_id',
            error: 'Boundary Tag A cannot be the same as the bin ID',
        });
    }
    if (bin && tagB && epcEquals(bin, tagB)) {
        return callback(null, {
            ok: false,
            reason: 'boundary_matches_bin_id',
            error: 'Boundary Tag B cannot be the same as the bin ID',
        });
    }

    const steps = [];

    if (bin) {
        steps.push(() =>
            checkEpcForRoleAsync(bin, { role: 'container_id', excludeBinId: exclude })
        );
    }
    if (tagA) {
        steps.push(() =>
            checkEpcForRoleAsync(tagA, { role: 'boundary', excludeBinId: exclude || bin })
        );
    }
    if (tagB) {
        steps.push(() =>
            checkEpcForRoleAsync(tagB, { role: 'boundary', excludeBinId: exclude || bin })
        );
    }

    (async () => {
        try {
            for (const step of steps) {
                const result = await step();
                if (!result.valid) {
                    return callback(null, {
                        ok: false,
                        reason: result.reason,
                        error: result.message,
                        epc: result.epc,
                        container_id: result.container_id,
                        container_name: result.container_name,
                        item_name: result.item_name,
                    });
                }
            }
            callback(null, { ok: true });
        } catch (err) {
            callback(err);
        }
    })();
}

function validateContainerSaveAsync(opts) {
    return new Promise((resolve, reject) => {
        validateContainerSave(opts, (err, result) => {
            if (err) reject(err);
            else resolve(result);
        });
    });
}

/** Maps registry reasons to near-field sniffer API reason codes. */
function toNearFieldReason(registryReason) {
    if (registryReason === 'registered_item') return 'already_registered';
    return registryReason;
}

module.exports = {
    normalizeEpc,
    normalizeBoundaryTag,
    normalizeExcludeId,
    epcEquals,
    boundaryTagsAreDuplicate,
    checkEpcForRole,
    checkEpcForRoleAsync,
    validateContainerSave,
    validateContainerSaveAsync,
    toNearFieldReason,
};
