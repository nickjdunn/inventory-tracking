# RFID Inventory System

Node.js + SQLite backend with desktop, mobile, and legacy Windows CE front ends for the **Nordic ID Merlin HTE00072** UHF handheld.

## Quick start

```bash
npm install
npm start
```

Server listens on port **3000** by default (`PORT` env override).

| UI | URL |
|----|-----|
| Desktop dashboard | http://localhost:3000/index.html |
| Modern mobile | http://localhost:3000/mobile.html |
| **Win CE (HTE00072)** | http://localhost:3000/win-ce.html |
| Test simulator (dev only) | http://localhost:3000/emulator.html |

---

## Nordic ID Merlin HTE00072 (Windows CE) Production Setup

Use this section when deploying the gun on your warehouse floor with the legacy Pocket Internet Explorer browser.

### Network parameters (Proxmox / LAN)

| Setting | Value |
|---------|--------|
| **Server ingestion endpoint** | `http://10.17.17.17:3000/api/hardware/merlin-wedge` |
| **Device browser bookmark** | `http://10.17.17.17:3000/win-ce.html` |
| **Scanner ID** | `HTE00072` (sent as `scanner_id` in JSON bodies) |
| **Heartbeat interval** | **30 seconds** via `hardware/merlin-client.js` |

Replace `10.17.17.17` with your Proxmox host or inventory server IP.

### 1. Start the backend on the server

On the Proxmox VM (or host):

```bash
cd /path/to/rfid-inventory-system
npm install
MERLIN_SERVER_IP=10.17.17.17 node server.js
```

Optional: run the hardware heartbeat helper so the dashboard shows the gun as online:

```bash
MERLIN_SERVER_IP=10.17.17.17 MERLIN_SCANNER_ID=HTE00072 node hardware/merlin-client.js
```

This posts to `/api/scanner/heartbeat` every **30 seconds**.

### 2. Configure Nordic ID RFID Wedge on the gun

On the Merlin (**HTE00072**), open **Nordic ID RFID Wedge** (or the vendor wedge utility shipped with the device).

Choose **one** of these patterns:

#### Option A — HTTP POST (recommended)

- **Mode:** HTTP POST / REST  
- **URL:** `http://10.17.17.17:3000/api/hardware/merlin-wedge`  
- **Content-Type:** `application/json` or `text/plain`  
- **Body (JSON example):**

```json
{
  "scanner_id": "HTE00072",
  "target_container_epc": "BIN-GARAGE-01",
  "scanned_tags": [
    { "epc": "EPC301833B2A1TOOL00001", "rssi": -52 }
  ]
}
```

Plain text is also accepted (comma or newline separated EPCs, optional `EPC|rssi` per line).

#### Option B — Keyboard wedge + manual submit on Win CE

- **Mode:** Keyboard wedge (EPCs typed into focused field)  
- On the gun browser, open **win-ce.html** → **Scan** tab  
- Paste wedge output into **Tag list** → **Submit tags to server**

For **Find mode**, wedge reads with RSSI should still hit the server (via HTTP POST or `/api/scan`) so the UI can show **CLOSE / WARM / COLD** from `GET /api/search/target`.

### 3. Bookmark the legacy UI on the Merlin

1. Open Pocket Internet Explorer on the device.  
2. Navigate to: **http://10.17.17.17:3000/win-ce.html**  
3. Add to Favorites.  
4. Use **Items** to search inventory, **Find** to hunt (live RSSI from server — no simulated signal on this page).

### 4. Find mode behavior (production)

1. On desktop or Win CE **Items**, tap **Find** on an item (sets hunt queue on server).  
2. Open **Find** tab on the gun.  
3. **Pull the trigger** while sweeping; wedge sends tags + RSSI to the server.  
4. Win CE polls **`GET /api/search/target`** every 1.5s and displays `hunt_signal.zone`:  
   - `CLOSE` / `WARM` / `COLD` / `NO_SIGNAL`  
5. Adjust RSSI gates under **Admin** if needed (`rssi_near_gate`, `rssi_far_gate`).

### 5. After testing with the simulator

All mock traffic and simulated Find RSSI belong in **emulator.html** only.

One-click cleanup:

- Open http://10.17.17.17:3000/emulator.html → **Purge All Test Data**, or  
- `DELETE http://10.17.17.17:3000/api/test/purge`

Removes rows where **EPC**, **name**, **description**, or container fields start with **`TEST-EPC-`**.

---

## API reference (hardware)

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/hardware/merlin-wedge` | Raw wedge / bulk tag ingest → spatial filter + inventory |
| `POST` | `/api/scan` | Structured scan (same processing as wedge) |
| `GET` | `/api/search/target` | Hunt queue + **live** `hunt_signal` for primary target |
| `POST` | `/api/search/target` | Set/clear hunt queue `{ "epc_ids": ["..."] }` |
| `POST` | `/api/scanner/heartbeat` | Scanner online ping |
| `DELETE` | `/api/test/purge` | Remove all `TEST-EPC-*` test data |

---

## Development vs production

| Feature | Production (`mobile.html`, `win-ce.html`) | Test only (`emulator.html`) |
|---------|-------------------------------------------|-----------------------------|
| Random / autopilot scans | Off | Available |
| Simulated Find RSSI / geiger audio | Off | Find Mode Test Rig |
| `TEST-EPC-*` tags | Avoid | Generated here |

---

## Environment variables

| Variable | Default | Description |
|----------|---------|-------------|
| `PORT` | `3000` | HTTP port |
| `MERLIN_SERVER_IP` | `10.17.17.17` | Target host for `merlin-client.js` |
| `MERLIN_SCANNER_ID` | `HTE00072` | Heartbeat + scan identity |
| `MERLIN_TARGET_BIN` | _(empty)_ | Default bin for bulk scans from client |

---

## License

ISC
