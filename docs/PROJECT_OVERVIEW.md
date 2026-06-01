# RFID Inventory System — Project Overview

## Architecture

```
┌─────────────────┐     HTTP/JSON      ┌──────────────────────────────────┐
│  Web UIs        │ ◄────────────────► │  server.js (Express, port 3000)  │
│  index, mobile, │                    │  + epc-registry.js (global EPC)  │
│  bins, admin    │                    │  + database.js → inventory.db    │
└─────────────────┘                    └──────────────────────────────────┘
         ▲                                          ▲
         │                                          │
┌────────┴────────┐                    ┌────────────┴────────────┐
│ emulator.html   │                    │ hardware/merlin-client  │
│ (dev simulator) │                    │ (Node bridge / reference)│
└─────────────────┘                    └────────────┬────────────┘
                                                    │
                                         ┌──────────▼──────────┐
                                         │ Nordic ID Merlin    │
                                         │ (Windows CE — TBD)  │
                                         └─────────────────────┘
```

**Single source of truth:** SQLite `inventory.db` on the Proxmox/host server. Handhelds and browsers are thin clients; they never own inventory state locally (except optional CE offline queue).

---

## Repository layout

| Path | Role |
|------|------|
| `server.js` | Express API, scan processing, dashboard, admin, static `public/` |
| `database.js` | Schema init + migrations (`items`, `containers`, `scan_history`, `system_settings`, UPC cache) |
| `epc-registry.js` | **Global EPC uniqueness** — item vs bin ID vs boundary tag (one EPC, one role) |
| `db-async.js` | Promise helpers for SQLite |
| `upc-lookup.js` | Open Food Facts / Open Beauty Facts / optional UPCitemdb chain |
| `emulator.js` | CLI dev tool that hits APIs like a scanner |
| `hardware/merlin-client.js` | Reference Node client: bulk RFID scan, near-field ingest, heartbeat |
| `public/index.html` | Desktop inventory dashboard |
| `public/mobile.html` | Touch-friendly handheld **browser** UI (not CE-native) |
| `public/bins.html` | Bin manager + boundary tag sniffer |
| `public/admin.html` | RSSI gates, HA webhook, UPC API key |
| `public/inventory-shared.js` | Shared filter/status/search/delete/replace-tag helpers |
| `public/onboarding-wizard.js` | Near-field “register new tag” modal |
| `public/boundary-sniffer.js` | Near-field boundary capture for bins |
| `public/emulator.html` | Browser hardware simulator |
| `docs/WINDOWS_CE_HANDHELD.md` | Native scanner app scope (trigger + keypad UPC) |
| `docs/MERLIN_DEPLOYMENT.md` | How to load CAB/exe onto Merlin (USB, SD, LAN) |

---

## Data model (summary)

- **`items`** — `epc_id` (PK), name, category, description, UPC, `container_id`, `home_container_id`
- **`containers`** — `id` (PK, often also an RFID), name, `boundary_tag_a`, `boundary_tag_b` (spatial zone)
- **`scan_history`** — audit log of MOVED / FOUND / REGISTERED / REJECTED
- **`system_settings`** — `rssi_near_gate`, `rssi_far_gate`, Home Assistant URL, etc.

**Status logic** (client + server): HOME, FLOATING, MISPLACED, UNASSIGNED from current vs home bin.

---

## HTTP API map

| Endpoint | Purpose |
|----------|---------|
| `POST /api/scan` | Bulk RFID read → update locations, spatial zone detection |
| `POST /api/scan/near-field-ingest` | Ultra-near reads (onboarding / sniffer) |
| `GET /api/scan/latest-near-field` | Poll best near-field tag since timestamp |
| `GET /api/epc/validate` | Check EPC against global registry |
| `GET /api/handheld/sync` | Compact snapshot for native handheld (items, bins, hunt queue, gates) |
| `GET /api/dashboard` | Full dashboard payload |
| `GET/POST /api/items` | List/create items |
| `PUT/DELETE /api/items/:epc_id` | Update / delete item |
| `POST /api/items/:epc_id/replace-epc` | Swap damaged RFID for new EPC |
| `GET/POST/PUT/DELETE /api/containers` | Bin CRUD |
| `GET/POST /api/search/target` | Batch hunt queue |
| `GET /api/upc/lookup/:code` | Product lookup by UPC |
| `POST /api/scanner/heartbeat` | Handheld online presence |
| `GET /api/scanner/status` | Dashboard “Merlin online” badge |
| `GET/POST /api/admin/settings` | Admin configuration |

---

## Scan pipeline

1. Handheld sends `scanned_tags: [{ epc, rssi }, …]` and optional `target_container_epc`.
2. Server records ultra-near reads into an in-memory buffer (onboarding).
3. If both boundary tags of a bin appear in the same read → **spatial zone** sets target bin automatically; boundary EPCs are stripped from item processing.
4. Known items: batch-loaded, then MOVED or FOUND in one DB pass.
5. Unknown EPCs: registry check; auto-register placeholder only if EPC is free system-wide.

---

## Web clients vs native CE

| | Web (`mobile.html`) | Native CE (planned) |
|--|---------------------|---------------------|
| Runtime | Browser on scanner | .NET CF / Compact Framework or vendor SDK app |
| RFID | Via bridge or browser limits | **Gun trigger** → UHF inventory API |
| UPC | Manual / camera | **Keypad scan button** → laser barcode SDK |
| UI | Full page, polling | Small screen, mode-based, minimal chrome |
| Offline | Requires Wi‑Fi | Optional local queue + sync |

See **`docs/WINDOWS_CE_HANDHELD.md`** for the full CE product scope.
