# Merlin native handheld app (v1.0)

**.NET Compact Framework 3.5** WinForms app for Nordic ID Merlin — **Receive**, **Find**, **Add**, **Settings**.

Output assembly: `MerlinInventoryTest.exe` (CAB name unchanged for Wi‑Fi deploy: `MerlinInventoryTest.cab`).

## Build (Visual Studio 2008 + Windows CE SDK)

1. Open `handheld-ce/MerlinInventoryTest/MerlinInventoryTest.csproj`
2. Platform: **Windows CE** / **ARMV4I** (match your Merlin)
3. **Build → Build Solution**
4. Create a **Smart Device CAB Project** → add primary output → build CAB

## Publish to server (Wi‑Fi install)

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
