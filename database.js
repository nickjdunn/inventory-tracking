const sqlite3 = require('sqlite3').verbose();
const path = require('path');

// Connect to the local database file (it will be created automatically if it doesn't exist)
const dbPath = path.join(__dirname, 'inventory.db');
const db = new sqlite3.Database(dbPath);

// Initialize the database tables
db.serialize(() => {
    // 1. Locations (e.g., Garage, Basement, Storage Unit)
    db.run(`CREATE TABLE IF NOT EXISTS locations (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT NOT NULL UNIQUE,
        description TEXT
    )`);

    // 2. Containers / Bins (Physical totes with their own RFID EPC)
    db.run(`CREATE TABLE IF NOT EXISTS containers (
        epc_id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        location_id INTEGER,
        FOREIGN KEY(location_id) REFERENCES locations(id)
    )`);

    // 3. Physical Items (The individual items with their own RFID EPC)
    db.run(`CREATE TABLE IF NOT EXISTS items (
        epc_id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        description TEXT,
        category TEXT,
        container_id TEXT,
        home_container_id TEXT,
        FOREIGN KEY(container_id) REFERENCES containers(epc_id)
    )`);

    // Backward-compatible migration for existing databases.
    try {
        db.run(`ALTER TABLE items ADD COLUMN home_container_id TEXT`, (err) => {
            if (err && !String(err.message || '').includes('duplicate column name')) {
                console.error('Failed to add home_container_id to items:', err.message);
            }
        });
    } catch (err) {
        if (!String(err.message || '').includes('duplicate column name')) {
            console.error('Failed to add home_container_id to items:', err.message);
        }
    }

    // 4. Scan History (The immutable timeline audit trail)
    db.run(`CREATE TABLE IF NOT EXISTS scan_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
        scanned_epc TEXT NOT NULL,
        parent_container_epc TEXT,
        action TEXT NOT NULL
    )`);

    console.log("💾 SQLite Database tables initialized successfully.");
});

module.exports = db;