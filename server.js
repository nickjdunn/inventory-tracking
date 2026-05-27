const express = require('express');
const db = require('./database'); // Import our database setup
const app = express();
const PORT = process.env.PORT || 3000;

app.use(express.json());

// 📡 The Endpoint: Processes bulk scans and updates item locations
app.post('/api/scan', (req, res) => {
    const { scanner_id, target_container_epc, scanned_tags } = req.body;

    console.log(`\n--- 📡 Processing Scan: ${scanned_tags ? scanned_tags.length : 0} tags ---`);

    if (!scanned_tags || scanned_tags.length === 0) {
        return res.status(200).json({ status: "success", message: "No tags received." });
    }

    // Prepare statement to log history
    const logStmt = db.prepare(`INSERT INTO scan_history (scanned_epc, parent_container_epc, action) VALUES (?, ?, ?)`);
    
    // Prepare statement to update an item's current container assignment
    const updateItemStmt = db.prepare(`UPDATE items SET container_id = ? WHERE epc_id = ?`);

    db.serialize(() => {
        scanned_tags.forEach((epc) => {
            // 1. Check if the item already exists in our master list
            db.get(`SELECT name, container_id FROM items WHERE epc_id = ?`, [epc], (err, item) => {
                if (err) console.error(err);

                if (item) {
                    // Item exists! Check if it moved bins
                    if (item.container_id !== target_container_epc) {
                        console.log(`📦 MOVED: '${item.name}' [${epc}] moved to bin [${target_container_epc}]`);
                        updateItemStmt.run(target_container_epc, epc);
                        logStmt.run(epc, target_container_epc, 'MOVED');
                    } else {
                        console.log(`🎯 CONFIRMED: '${item.name}' is still in bin [${target_container_epc}]`);
                        logStmt.run(epc, target_container_epc, 'FOUND');
                    }
                } else {
                    // First time seeing this tag! Auto-register it as an Unnamed Item
                    console.log(`🆕 UNKNOWN TAG DETECTED: [${epc}]. Creating placeholder entry.`);
                    
                    db.run(`INSERT INTO items (epc_id, name, container_id) VALUES (?, ?, ?)`, 
                        [epc, `Unknown RFID Tag (${epc.slice(-4)})`, target_container_epc]
                    );
                    logStmt.run(epc, target_container_epc, 'REGISTERED');
                }
            });
        });
    });

    res.status(200).json({ 
        status: "success", 
        message: `Database processed ${scanned_tags.length} tags.` 
    });
});

app.listen(PORT, () => {
    console.log(`🚀 RFID Backend Server with DB active on port ${PORT}`);
});