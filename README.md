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

1. On desktop, mobile, or Win CE **Items**, tap **Find** on one or more items (adds to the server hunt queue).  
2. Open **Find** — you see **side-by-side radar columns** (one per target).  
3. **Pull the trigger** while sweeping; wedge sends tags + RSSI to the server.  
4. The server pushes updates in **under ~200ms** via:
   - **WebSocket** `ws://HOST:3000/api/hunt/ws` (mobile / modern browsers)
   - **Long-poll** `GET /api/search/target?wait=1&rev=N` (Win CE Pocket IE fallback)
   - **Fast compact poll** `GET /api/search/target?rev=N&compact=1` (~120ms) if long-poll fails  
5. Each column shows zone color: **Green** CLOSE, **Amber** WARM, **Blue** COLD, **Gray** NO_SIGNAL.  
6. On Win CE, **arrow keys** move focus across columns; touch still works.  
7. Adjust RSSI gates under **Admin** if needed (`rssi_near_gate`, `rssi_far_gate`).

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
| `GET` | `/api/search/target` | Hunt queue + `hunt_targets[]` + `revision` (supports `?wait=1&rev=N` long-poll, `?compact=1`) |
| `POST` | `/api/search/target` | Set/clear multi-target hunt queue `{ "epc_ids": ["..."] }` |
| `WS` | `/api/hunt/ws` | Push hunt payload on every wedge RSSI update |
| `GET` | `/api/hunt/stream` | Server-Sent Events hunt stream (fallback) |
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
