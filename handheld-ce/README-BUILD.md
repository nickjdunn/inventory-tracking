# Build MerlinInventoryTest.cab (first native test)

This is a **.NET Compact Framework 3.5** app that only tests HTTP to your inventory server. No RFID yet.

## What you need on the PC

- **Visual Studio 2008** with **Smart Device Programmability** (Windows Mobile 6 / CE SDK)
- Or open the `.csproj` and retarget to your installed CE platform if VS prompts you

If you do not have VS2008, use the **browser test only** (no build):

`http://<server-ip>:3000/deploy/ce-wifi-test.html`

## Build steps (VS2008)

1. Open `handheld-ce/MerlinInventoryTest/MerlinInventoryTest.csproj`.
2. When prompted, select platform **Windows CE** / **Professional** matching your Merlin (usually **ARMV4I**).
3. Menu **Build → Build Solution**.
4. Menu **Build → Deploy** (USB) **or** create CAB:
   - **File → New → Project → Setup and Deployment → Smart Device CAB Project**
   - Add primary output of `MerlinInventoryTest`
   - Build CAB → `MerlinInventoryTest.cab`

## Publish to Wi‑Fi deploy folder

From repo root (PowerShell):

```powershell
.\scripts\publish-to-deploy.ps1 -CabPath "C:\path\to\MerlinInventoryTest.cab"
```

Or manually copy `MerlinInventoryTest.cab` to:

`public/deploy/MerlinInventoryTest.cab`

Restart server (if running). Open on Merlin browser:

`http://<server-ip>:3000/deploy/`

## On the Merlin

1. **Browser test:** `/deploy/ce-wifi-test.html` → Run all tests.
2. **Native app:** tap CAB link → install → run **Merlin Inventory Test** from Programs.
3. Enter server `10.17.17.17:3000` (or your IP) → **Test connection**.

Dashboard should show scanner **merlin-ce-native-test** online after a successful heartbeat.

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Project won't load | Create new Smart Device WinForms app, paste `Program.cs` + `MainForm.cs` |
| HTTP fails on device | Ping server IP; check Wi‑Fi; disable HTTPS (use `http://`) |
| CAB won't install | Wrong CPU (need ARM CE CAB); try USB + WMDC instead |
| No .cab link on deploy page | CAB not copied to `public/deploy/` |

See `docs/MERLIN_WIFI_DEPLOY.md` for NAS vs HTTP update workflow.
