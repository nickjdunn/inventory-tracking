const sqlite3 = require('sqlite3').verbose();
const path = require('path');

const dbPath = path.join(__dirname, 'inventory.db');
const db = new sqlite3.Database(dbPath);

db.serialize(() => {
    db.run(`CREATE TABLE IF NOT EXISTS locations (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT NOT NULL UNIQUE,
        description TEXT
    )`);

    db.run(`CREATE TABLE IF NOT EXISTS containers (
        id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        description TEXT
    )`, (err) => {
        if (err) console.error('containers init:', err.message);
        else migrateLegacyContainersTable();
    });

    db.run(`CREATE TABLE IF NOT EXISTS items (
        epc_id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        description TEXT,
        category TEXT,
        container_id TEXT,
        home_container_id TEXT,
        FOREIGN KEY(container_id) REFERENCES containers(id),
        FOREIGN KEY(home_container_id) REFERENCES containers(id)
    )`);

    db.run(`ALTER TABLE items ADD COLUMN home_container_id TEXT`, (err) => {
        if (err && !String(err.message || '').includes('duplicate column name')) {
            console.error('home_container_id migration:', err.message);
        }
    });

    db.run(`CREATE TABLE IF NOT EXISTS scan_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
        scanned_epc TEXT NOT NULL,
        parent_container_epc TEXT,
        action TEXT NOT NULL
    )`);

    db.run(`CREATE TABLE IF NOT EXISTS system_settings (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
    )`, (err) => {
        if (err) console.error('system_settings init:', err.message);
        else seedDefaultSystemSettings();
    });

    console.log('💾 SQLite Database tables initialized successfully.');
});

const DEFAULT_SYSTEM_SETTINGS = {
    home_assistant_url: '',
    enable_ha_notifications: 'false',
    rssi_near_gate: '-55',
    rssi_far_gate: '-85',
};

function seedDefaultSystemSettings() {
    const stmt = db.prepare(
        `INSERT OR IGNORE INTO system_settings (key, value) VALUES (?, ?)`
    );
    Object.entries(DEFAULT_SYSTEM_SETTINGS).forEach(([key, value]) => {
        stmt.run(key, value);
    });
    stmt.finalize();
}

function migrateLegacyContainersTable() {
    db.all(`PRAGMA table_info(containers)`, [], (err, columns) => {
        if (err) return;
        const hasEpcId = columns.some((c) => c.name === 'epc_id');
        const hasId = columns.some((c) => c.name === 'id');
        if (!hasEpcId || hasId) return;

        console.log('💾 Migrating legacy containers table (epc_id → id + description)...');
        db.serialize(() => {
            db.run(`CREATE TABLE containers_migrated (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT
            )`);
            db.run(
                `INSERT INTO containers_migrated (id, name, description)
                 SELECT epc_id, name, NULL FROM containers`
            );
            db.run(`DROP TABLE containers`);
            db.run(`ALTER TABLE containers_migrated RENAME TO containers`);
        });
    });
}

module.exports = db;
