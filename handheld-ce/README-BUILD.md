# Merlin native handheld app (v1.0)

**.NET Compact Framework 3.5** WinForms app for Nordic ID Merlin — **Receive**, **Find**, **Add**, **Settings**.

Output assembly: `MerlinInventoryTest.exe` (CAB name unchanged for Wi‑Fi deploy: `MerlinInventoryTest.cab`).

## Build (Visual Studio 2008 + Windows CE SDK)

Every build stamps **git** into the app (`1.0.&lt;commit-count&gt;+&lt;short-hash&gt;`). On the gun, **Set → Check git version** compares with the server deploy API.

**One command** (after `scripts\install-handheld-prereqs.ps1` as Administrator):

```powershell
.\scripts\build-handheld.ps1
```

This builds **Release** for **Windows CE 6.0 / ARMV4I** (Nordic ID Merlin — *not* Windows Mobile / Pocket PC), packages `MerlinInventoryTest.cab`, and copies it to `public/deploy/`.

Manual steps in VS2008: open `MerlinInventoryTest.csproj` → **Build → Build Solution** → CAB via `Cabwiz.exe MerlinInventoryTest.inf /cpu ARMV4I` in `handheld-ce/MerlinInventoryTest/cab/`.

**Important:** Plain `makecab` only compresses the `.exe` — the gun will say *not a valid Windows CE setup file*. The INF + **Cabwiz** build produces a real installer CAB (~50 KB, not ~21 KB).

## Publish to server (Wi‑Fi install)

`build-handheld.ps1` publishes automatically. To copy an existing CAB:

```powershell
.\scripts\publish-to-deploy.ps1 -CabPath "path\to\MerlinInventoryTest.cab"
```

On gun: `http://<server>:3000/deploy/` → install CAB.

## Using the app

| Control | Action |
|---------|--------|
| **Receive** | Pick bin → paste/type EPCs (wedge) → **Send tags (Trigger)** |
| **Find** | Search → **Hunt selected** → F1 = simulate RFID read |
| **Add** | F2 = UPC (Scan key) → Lookup → F1 = EPC (Trigger) → Register |
| **Find** | Select item → **Swap tag** → F1 scan new EPC (damaged tag replacement) |
| **Set** | Server URL, scanner ID, **Sync inventory** |

Until Nordic RFID/Barcode SDK is wired:

- **F1** = Trigger / RFID (prompt for EPC list)
- **F2** = Scan key / UPC (Add mode)

When wedge posts tags into a text field automatically, paste into **Receive** tag box or we hook `HardwareBridge.cs` (next step) to Nordic samples.

## Config file

Saved next to the exe: `merlin-handheld.cfg`

```
server=http://10.17.17.17:3000
scanner=merlin-handheld-01
bin=BIN-GARAGE-01
mode=Receive
```

## Next integration step

Copy Nordic **NUR API** trigger events into a new `RfidReader.cs` that calls:

- Receive/Find → fill tag text → `PostScan`
- Add → single EPC → `ValidateEpc` + register

Barcode SDK → `AddPanel.SetUpc()` on scan button event.
