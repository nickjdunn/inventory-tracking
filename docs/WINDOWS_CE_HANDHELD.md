# Windows CE Handheld App — Product Scope

**Deploying to the physical scanner:** see **`docs/MERLIN_DEPLOYMENT.md`** (CAB/USB/SD/LAN, prerequisites, what not to install).

Native application for **Nordic ID Merlin** (Windows CE / Windows Mobile 6.x class devices) replacing the browser-based `mobile.html` on the scanner. Optimized for a **small QVGA/VGA screen**, **one-handed operation**, and **two distinct scan inputs** without mode confusion.

---

## Goals

1. **Faster than web** — no browser chrome, no 2s dashboard polling; event-driven UI on trigger and barcode.
2. **Dual-input workflow** — physical **trigger = RFID (UHF)**; keypad **Scan = UPC (1D laser)**.
3. **Same backend** — all state on Proxmox `server.js`; CE app is a dedicated client like `merlin-client.js` but with UI.
4. **Global EPC rules** — every new tag validated via `GET /api/epc/validate` before register (items cannot share EPCs with bins/boundaries).

---

## Non-goals (v1)

- Full bin manager / boundary programming on device (keep on desktop `bins.html`).
- Replacing desktop dashboard.
- Running Node on the scanner (use native vendor SDK + HTTP).

---

## Hardware integration map

| Input | CE source | App behavior | Backend |
|-------|-----------|--------------|---------|
| **Gun trigger (press/hold)** | Nordic ID UHF API / `RFID` module events | Inventory read → tag list + RSSI | `POST /api/scan` |
| **Gun trigger (short / release)** | Same, optional near-field profile | Single strongest tag if RSSI ≥ near gate | `POST /api/scan/near-field-ingest` + poll `latest-near-field` only in Register mode |
| **Keypad Scan button** | Barcode SDK (`Symbol`, `Honeywell`, or Nordic companion laser) | UPC string | `GET /api/upc/lookup/:code` then create/link item |
| **Wi‑Fi** | CE connection manager | Queue requests when offline | Local SQLite queue → replay |
| **Battery / dock** | System APIs | Optional metadata on heartbeat | `POST /api/scanner/heartbeat` |

### Trigger vs Scan button — UX model

```
┌─────────────────────────────────────────┐
│  MODE: [ Receive ] [ Find ] [ Add ]     │  ← soft keys or tabs
├─────────────────────────────────────────┤
│  Active bin: GARAGE-TOTE-01      [▼]    │
│  Last action: 12 tags → bin (0.4s)      │
│  ┌─────────────────────────────────┐    │
│  │  (context list / hunt / form)    │    │
│  └─────────────────────────────────┘    │
├─────────────────────────────────────────┤
│  TRIGGER = RFID    │   SCAN = UPC       │  ← always visible hint
└─────────────────────────────────────────┘
```

- **Trigger** never starts barcode decode; **Scan** never starts UHF inventory (configure SDKs so they are exclusive).
- Audio/haptic: short beep on UPC success; double beep on RFID batch complete; error tone on registry conflict.

---

## Recommended screens (v1)

### 1. Receive (default floor mode)

- Select **target bin** (dropdown synced from server; remember last used).
- **Hold trigger** → continuous or single-shot inventory (device capability).
- On release: POST all tags to `/api/scan` with `target_container_epc`.
- Show: count moved / confirmed / rejected / new unknown; last spatial zone if boundaries detected.
- **No form** — speed is priority.

### 2. Find (hunt)

- Search box (instant filter local cache from `/api/handheld/sync`).
- Tap item → set hunt queue `POST /api/search/target` with one EPC.
- **Hold trigger** → read tags; highlight when scanned EPC matches queue (RSSI meter optional using last read RSSI).
- Geiger-style audio optional (port from mobile web logic).

### 3. Add item (UPC + RFID)

- **Press Scan (UPC)** → lookup → show name/category preview.
- **Hold trigger (RFID)** → capture EPC (near-field gate) → validate `role=item`.
- Confirm → `POST /api/items`.
- Skip UPC path: manual name + trigger EPC only.

### 4. Quick register tag (RFID only)

- Near-field listen UI (like onboarding wizard).
- `near-field-ingest` on each trigger pull; poll `latest-near-field?purpose=onboarding`.
- Minimal fields: name + home bin → `POST /api/items`.

### 5. Settings

- Server IP / port, scanner ID, default bin, RSSI display toggle, test connection, sync now.

---

## Backend contract (already implemented / planned)

| Need | API |
|------|-----|
| Boot / periodic sync | `GET /api/handheld/sync` |
| RFID bulk | `POST /api/scan` |
| Near-field buffer | `POST /api/scan/near-field-ingest`, `GET /api/scan/latest-near-field` |
| UPC | `GET /api/upc/lookup/:code` |
| Register item | `POST /api/items` |
| Replace tag | `POST /api/items/:epc/replace-epc` |
| Hunt queue | `GET/POST /api/search/target` |
| Online badge | `POST /api/scanner/heartbeat` every 30s |
| EPC check | `GET /api/epc/validate?epc=&role=item` |

Reference implementation for HTTP shape: `hardware/merlin-client.js`.

---

## Suggested tech stack on Windows CE

| Layer | Option A (common on Merlin) | Option B |
|-------|------------------------------|----------|
| UI | **C# .NET Compact Framework 3.5** WinForms | C++ with MFC (vendor samples) |
| HTTP | `System.Net` or `[opennetcf]` | WinInet wrapper |
| UHF RFID | Nordic ID **RFID SDK** / sample app patterns | — |
| 1D barcode | **Symbol / Zebra EMDK** or Nordic laser API | Keyboard wedge (avoid — loses RSSI pairing) |
| Local queue | SQL CE Compact or flat file JSON | — |

**Do not use** embedded WebBrowser control for main UI — memory and input routing are poor on CE; native forms fit the screen better.

---

## Offline / resilience

1. **Outbound queue** — if POST fails, store `{ endpoint, body, created_at }` locally; retry with exponential backoff when Wi‑Fi returns.
2. **Inbound cache** — `/api/handheld/sync` snapshot refreshed on connect and every N minutes; Find mode searches cache first.
3. **Idempotency** — bulk scan POSTs may be retried; server MOVED/FOUND is safe to repeat.
4. **Heartbeat** — cheap `POST /api/scanner/heartbeat` so dashboard shows device online.

---

## Performance targets

| Action | Target |
|--------|--------|
| Trigger release → UI feedback | &lt; 100 ms (local) |
| Trigger release → server ACK | &lt; 800 ms on LAN |
| UPC Scan → product name shown | &lt; 1.5 s (cache hit faster) |
| Cold sync snapshot | &lt; 3 s for 5k items |

---

## Implementation phases

### Phase 1 — Connectivity shell (1–2 weeks)

- WinForms shell, settings, heartbeat, manual IP test.
- Trigger → read tags → `POST /api/scan` with selected bin.
- List last result counts.

### Phase 2 — UPC path (1 week)

- Keypad Scan → lookup → create item with trigger-bound EPC.
- Registry validation before save.

### Phase 3 — Find + sync (1 week)

- `/api/handheld/sync` cache, hunt mode, RSSI feedback.

### Phase 4 — Polish

- Offline queue, spatial zone message on UI, replace-EPC flow, sound profile.

---

## Security (LAN)

- v1: HTTP on private VLAN (matches current server).
- v2 (optional): API key header per scanner in `system_settings`.

---

## Relationship to existing code

| Component | Handheld uses |
|-----------|----------------|
| `server.js` | All APIs |
| `epc-registry.js` | Enforced server-side; CE calls validate for UX |
| `merlin-client.js` | Copy HTTP payload shapes, not Node runtime |
| `mobile.html` | Feature reference only — do not ship WebKit on device |

---

## Open decisions (for you)

1. **Continuous vs single-shot RFID** on trigger hold — depends on Merlin firmware profile.
2. **Default mode on boot** — Receive vs last used.
3. **Whether CE app allows bin creation** — recommend no for v1.
4. **Device-specific SDK version** from Nordic ID support portal for your exact CE build.
