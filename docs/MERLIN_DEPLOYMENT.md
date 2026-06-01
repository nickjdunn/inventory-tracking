# Loading software onto the Nordic ID Merlin (Windows CE)

Use this **before** building the native inventory app. The Merlin does not run Node.js or your Proxmox server — it only runs **Windows CE binaries** (and Nordic **PAK** patches for drivers/firmware).

---

## What gets installed where

| Component | Runs on | How it gets there |
|-----------|---------|-------------------|
| **Inventory backend** (`server.js`) | Proxmox / LAN server | `npm start` on the host (already your setup) |
| **Web UIs** (`public/*.html`) | Server, viewed in a browser | Served by Express — optional on scanner browser only |
| **Native handheld app** (planned) | Merlin (`\Program Files\...` or `\Flash\...`) | **.cab** or copied **.exe** + DLLs |
| **RFID / Wi‑Fi drivers** | Merlin firmware layer | Nordic **.pak** files → `\Flash` → cold boot |
| **Dev reference** (`merlin-client.js`) | PC only | Not installed on scanner |

The scanner talks to the server over **Wi‑Fi (HTTP)**. Deployment = put the CE app on the gun; point it at `http://<server-ip>:3000`.

---

## Prerequisites on the Merlin (do once)

1. **Confirm OS version**  
   Settings → System, or Nordic documentation. Merlin is typically **Windows CE 6.x** class (build like 3.7.x device software). Your app must target the **same CPU** (ARM) and **.NET Compact Framework** version the device has (often **3.5**).

2. **Install / update Nordic RFID stack** (if not already current)  
   - Download patches from [Nordic ID Merlin downloads](https://www.nordicid.com/support/devices-downloads/nordic-id-merlin/patches-for-nordic-id-merlin/).  
   - Copy **`.pak`** files to the device **`\\Flash`** folder (via USB or ActiveSync).  
   - **Cold boot** (remove battery / hard reset per Nordic instructions).  
   - Include **NUR driver** PAK if you will use the UHF API in your app.  
   - **RFID Demo** / **RFID CPL** on the device are useful to verify trigger + reads before your app exists.

3. **Network**  
   - Configure Wi‑Fi on the Merlin so it can reach the server (e.g. `10.17.17.17:3000`).  
   - Open the device browser once and hit `http://<server-ip>:3000/mobile.html` to prove connectivity (optional sanity check).

4. **Barcode (UPC) stack**  
   - If the Merlin has an integrated laser, install the **vendor barcode SDK/driver** your unit shipped with (often preinstalled).  
   - Map the physical **Scan** key to the barcode engine (not keyboard wedge) in your app when you integrate SDK.

---

## Development machine (PC) — what you install

| Tool | Purpose |
|------|---------|
| **Visual Studio 2008** (or VS 2005 + CE SDK) with **Smart Device / Windows Mobile** workload | Build **.NET Compact Framework 3.5** apps for ARM |
| **Nordic ID SDK / NUR API** samples for Windows CE | Trigger + UHF inventory code |
| **Windows Mobile Device Center** (Win 7) or legacy **ActiveSync** (XP) | USB deploy to device |
| Optional: **Platform Builder** / device images | Only if you customize CE images (usually not needed) |

Modern VS 2022 does **not** target Windows CE out of the box — teams often keep a **VM with Win7 + VS2008** for CE builds, or use Nordic’s sample projects as a template.

**Build outputs you will deploy:**

- `MerlinInventory.exe` + `.dll` dependencies  
- Or a single **`MerlinInventory.cab`** (recommended for installs and updates)

The CAB must be built for **Windows CE / ARM**, not x64 Windows.

---

## Deployment methods (choose one primary)

### Method A — USB + Windows Mobile Device Center (most common)

Best for first install and debugging.

1. Install **Windows Mobile Device Center** on Windows 7–10 (Microsoft discontinued CE sync; WMDC 6.1 often still works on Win10 with drivers).
2. Connect Merlin by **USB** (cradle or cable). Wait until the PC shows **Connected**.
3. In WMDC: **File Management** → browse device storage.
4. Copy to device (typical folders):
   - **`MerlinInventory.cab`** → `\` (root) or `\Temp\` or `\Application\`
   - Or copy **`MerlinInventory.exe`** + DLLs → `\Program Files\MerlinInventory\`
5. On the **device**:
   - Open **File Explorer** → tap the **.cab** → follow prompts → install to **Device** (not storage card unless you prefer).
   - Or run: **Start → Run** (if available) or tap **.exe** in Explorer.
6. Create a **Start menu shortcut** (installer or manual) for floor staff.

**Silent install (IT-style):** from Start → Run on device:

```text
\windows\wceload.exe /noaskdest "\MerlinInventory.cab"
```

(Adjust path to where you copied the CAB.)

---

### Method B — microSD / USB mass storage (no PC sync)

Good for field updates without a development PC.

1. Copy **`MerlinInventory.cab`** (or folder with exe + dlls) to a **FAT32** microSD.
2. Insert card in Merlin → File Explorer → open CAB → install.  
3. Or copy files from `\Storage Card\...` to `\Program Files\MerlinInventory\` and run exe.

---

### Method C — LAN drop (good for your warehouse)

Merlin on same VLAN as server.

1. Share a folder on the server or NAS: e.g. `\\10.17.17.17\deploy\MerlinInventory.cab`.
2. On device File Explorer (if CE networking + SMB works), navigate to share and tap CAB.  
   **Or** host CAB on HTTP:

   ```text
   http://10.17.17.17:3000/deploy/MerlinInventory.cab
   ```

   (Add a static `public/deploy/` folder on Express when you have a build — optional.)

3. Download with CE browser or copy tool → install locally.

---

### Method D — Nordic / partner tools

Some sites use Nordic’s **RFID Studio** or OEM provisioning tools to push apps during staging. Use if your reseller documented a standard image — otherwise A or B is enough.

---

## What you do **not** install on the scanner

| Do not | Why |
|--------|-----|
| `node server.js` | Node is not available on CE |
| `npm install` on device | Same |
| Full git repo | Only the built CAB/exe |
| `merlin-client.js` | PC reference only; native app reimplements HTTP |

---

## Updates (new version rollout)

1. Bump version in app (show on Settings screen).
2. Build new **.cab** on PC.
3. Deploy via A/B/C — uninstall old version first if CAB does not overwrite cleanly (Settings → Remove Programs on CE).
4. Server API can stay backward compatible; handheld calls `/api/handheld/sync` and `/api/scan`.

Keep **one** known server IP in app config (or DHCP + admin setting) — same as `MERLIN_SERVER_IP` in `hardware/merlin-client.js`.

---

## Verification checklist (after install)

| Step | Pass criteria |
|------|----------------|
| App launches | No missing DLL / CF version error |
| Wi‑Fi | Settings → ping server or app “Test connection” |
| Heartbeat | Dashboard shows **Merlin Scanner Online** (`POST /api/scanner/heartbeat`) |
| Trigger | RFID Demo or your app receives EPC + RSSI |
| Scan key | Barcode returns UPC string into app (not browser) |
| Bulk scan | `POST /api/scan` updates inventory on dashboard |

---

## Relationship to current `mobile.html`

Until the native app ships, operators can open **`http://<server-ip>:3000/mobile.html`** in the device browser — **no install**, but worse UX and no dedicated trigger/Scan routing.

The native app replaces that browser workflow; the server stays unchanged.

---

## Suggested folder layout on device (target)

```text
\Program Files\MerlinInventory\
    MerlinInventory.exe
    (NUR / barcode DLLs as required)
\Flash\
    (Nordic .pak patches only — not your app)
```

Config file (optional): `\Program Files\MerlinInventory\config.xml` with `ServerUrl`, `ScannerId`, `DefaultBin`.

---

## Wi‑Fi install (easiest updates)

See **`docs/MERLIN_WIFI_DEPLOY.md`**. Quick start:

1. Browser test (no CAB): `http://<server-ip>:3000/deploy/ce-wifi-test.html`
2. After building CAB: copy to `public/deploy/MerlinInventoryTest.cab` → open `http://<server-ip>:3000/deploy/` on the gun.

---

## Next step before coding

1. USB-connect Merlin to PC and confirm **File Management** works.  
2. Run Nordic **RFID Demo** on device — confirm trigger reads tags.  
3. Press **Scan** — confirm barcode enters a test field.  
4. Note exact **CE version**, **.NET CF version**, and **NUR API** sample path from Nordic SDK zip.  
5. Then start Visual Studio smart device project (see `docs/WINDOWS_CE_HANDHELD.md` for app scope).
