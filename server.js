const express = require('express');
const app = express();
const PORT = process.env.PORT || 3000;

// Tell express to automatically parse incoming JSON data payloads
app.use(express.json());

// 📡 The Endpoint: This receives the bulk tag scans from your Nordic reader
app.post('/api/scan', (req, res) => {
    const { scanner_id, target_container_epc, scanned_tags } = req.body;

    console.log(`\n--- 📡 Incoming RFID Scan Event ---`);
    console.log(`Scanner Device : ${scanner_id || 'Unknown'}`);
    console.log(`Target Container: ${target_container_epc || 'None (Open Scan)'}`);
    console.log(`Tags Detected   : ${scanned_tags ? scanned_tags.length : 0} items`);
    
    if (scanned_tags && scanned_tags.length > 0) {
        console.log(`EPC List        :`, scanned_tags);
    }

    // TODO: Connect database queries here to update item locations dynamically
    
    // Send a success response back to the Nordic handheld scanner
    res.status(200).json({ 
        status: "success", 
        message: `Processed ${scanned_tags ? scanned_tags.length : 0} tags successfully.` 
    });
});

// Start the server listener
app.listen(PORT, () => {
    console.log(`🚀 RFID Backend Server running locally on port ${PORT}`);
});