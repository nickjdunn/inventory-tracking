# Build the native Merlin app — step-by-step (beginner)

## Can Cursor / the AI build the `.cab` for you?

**No.** The native app must be compiled on a Windows PC with **Visual Studio 2008** and the **Windows Mobile / Windows CE SDK**. That toolchain targets the Merlin’s old ARM processor. Your repo already has all the **source code**; what’s missing is that one-time build environment.

**What works today without building:** the **browser** UI on the gun:

- `http://10.17.17.17:3000/deploy/` (hub)
- `http://10.17.17.17:3000/mobile.html` (full workflow)

The native app is the same features in a faster, dedicated program — worth doing when you have an afternoon to set up the build PC.

---

## Overview (3 phases)

```text
Phase A — Install build tools (once, ~1–2 hours)
Phase B — Build .exe + .cab on PC (~15 minutes)
Phase C — Copy .cab to server & install on Merlin (~5 minutes)
```

---

## Phase A — Install build tools

### Option 1: You already have an old Windows 7/10 PC with VS2008

If yes, skip to [Phase B](#phase-b--build-the-app).

### Option 2: Windows 11/10 today (recommended path)

Microsoft’s modern Visual Studio **cannot** build .NET Compact Framework 3.5 for Windows CE. Use a **Windows 7 or 10 VM** (or spare old laptop) for the build only.

1. **Enable virtualization** (Hyper-V or VMware Player — free).
2. Create a VM:
   - **Windows 10 x64** (easier) or Windows 7
   - 40 GB disk, 4 GB RAM minimum
3. Inside the VM, install in this order:

#### Step A1 — Visual Studio 2008

- Search for **“Visual Studio 2008 ISO”** or use an existing installer from your org.
- Run setup → workload: **Visual C#** (full or Express if that’s all you have).
- Install to default path.

#### Step A2 — Windows Mobile 6 SDK (includes CE smart device support)

- Search **“Windows Mobile 6 Professional SDK”** (Microsoft download archive / MSDN).
- Install after VS2008.
- Re-run VS2008 setup if it offers **Smart Device Programmability** — enable it.

#### Step A3 — Verify in Visual Studio 2008

1. Open Visual Studio 2008.
2. **File → New → Project**.
3. Look under **Visual C# → Smart Device**.
4. If you see **Smart Device Project**, the SDK is installed. If not, SDK step failed — reinstall Mobile 6 SDK.

---

## Phase B — Build the app

All paths below assume the project is on the build PC, e.g. copied from:

`inventory-tracking\handheld-ce\MerlinInventoryTest\`

### Step B1 — Open the project

1. Start **Visual Studio 2008** (not VS 2022).
2. **File → Open → Project/Solution**.
3. Browse to:
   `handheld-ce\MerlinInventoryTest\MerlinInventoryTest.csproj`
4. Click **Open**.

### Step B2 — Choose the device platform

1. When prompted for **platform**, pick the one that matches your Merlin (usually):
   - **Windows CE**
   - **Professional** or **Industrial**
   - CPU: **ARMV4I** (common on Nordic Merlin)
2. If unsure, check Nordic docs for **HTE00072** / Merlin Windows CE build.
3. Click **Finish** if it asks to convert — use defaults.

### Step B3 — Build the executable

1. Menu **Build → Configuration Manager**.
2. Set **Active solution configuration** to **Release**.
3. Menu **Build → Build MerlinInventoryTest**.
4. Success looks like:
   `Build: 1 succeeded, 0 failed`
5. Output exe is typically:
   `handheld-ce\MerlinInventoryTest\bin\Release\MerlinInventoryTest.exe`

If build fails with missing references, confirm **.NET Compact Framework 3.5** is selected in project properties.

### Step B4 — Create a CAB (installer package)

Visual Studio needs a **CAB project** (separate from the app project).

1. **File → Add → New Project**.
2. Category: **Other Project Types → Setup and Deployment** (or **Smart Device CAB Project** if listed).
3. If **Smart Device CAB Project** exists:
   - Name: `MerlinInventorySetup`
   - Add to solution, OK.
4. In Solution Explorer, right-click the CAB project → **Add → Project Output**.
5. Select **MerlinInventoryTest** → **Primary Output** → OK.
6. Right-click CAB project → **Build**.
7. Output file:
   `MerlinInventorySetup\bin\Release\MerlinInventorySetup.cab`
   (name may vary — use the `.cab` in the Release folder)

**Rename** (optional) to `MerlinInventoryTest.cab` to match the server deploy folder.

#### No CAB project template?

Copy these files to the gun via USB instead:

- `MerlinInventoryTest.exe`
- Any `.dll` files in the same `bin\Release` folder

Run the exe from `\Program Files\MerlinInventory\` on the device. CAB is nicer for reinstalls but not strictly required.

---

## Phase C — Install on the Merlin

### Wi‑Fi (easiest — you already proved this works)

1. On your **inventory server** (10.17.17.17), copy the CAB:
   ```text
   inventory-tracking\public\deploy\MerlinInventoryTest.cab
   ```
   PowerShell on your dev PC (from repo root):
   ```powershell
   .\scripts\publish-to-deploy.ps1 -CabPath "FULL_PATH_TO\MerlinInventoryTest.cab"
   ```
2. Restart Node server if it’s running: `npm start`
3. On the **Merlin browser**:
   - Open `http://10.17.17.17:3000/deploy/`
   - Tap **Install native test app** when the link appears
   - Tap the downloaded `.cab` → install to **Device**
4. **Start → Programs → Merlin Inventory** (desktop shortcut **Merlin Inv** on newer CABs), or File Explorer → `\Program Files\MerlinInventory\MerlinInventoryTest.exe`.
5. Stuck install? **Set → Exit app**, then deploy page → **Force uninstall (.cab)** → reinstall.

### USB (if Wi‑Fi CAB download fails)

1. Connect Merlin with USB + **Windows Mobile Device Center** on PC.
2. Copy `MerlinInventoryTest.cab` to `\Temp\` on the device.
3. File Explorer on gun → tap CAB → install.

---

## First launch checklist

1. Open app → **Set** tab.
2. Server: `http://10.17.17.17:3000` (your IP).
3. Scanner ID: e.g. `merlin-handheld-01`.
4. Tap **Save** → **Sync inventory** (wait for item/bin counts).
5. **Receive** tab → pick bin → pull RFID trigger (or F1) → tags post to server.
6. On desktop dashboard, confirm scanner shows online.

### Hardware (RFID trigger, barcode, hunt-only sync)

The native app uses **HardwareBridge** (`HardwareBridge.cs`, `NurApiBridge.cs`, `WedgeInputCapture.cs`):

| Input | Behavior |
|-------|----------|
| RFID trigger / F1 | Nordic **NUR** inventory when `NurApi.dll` is on device; otherwise keyboard **wedge** capture |
| Scan key / F2 (Add mode) | Barcode wedge for UPC |
| **Set → Refresh hunt only** | `GET /api/handheld/sync-summary` — hunt queue only, not full inventory |
| **Find → Refresh hunt** | Same lightweight hunt sync |

Settings still offers **full Sync inventory** (`/api/handheld/sync`). On weak Wi‑Fi, use hunt-only refresh while hunting.

---

## Troubleshooting

| Problem | What to do |
|---------|------------|
| No “Smart Device” in VS2008 | Install Windows Mobile 6 SDK; repair VS2008 |
| Build fails CS0246 / missing types | Project not set to .NET CF 3.5 — check Properties |
| CAB won’t install on gun | Wrong CPU (need ARM CAB); rebuild with ARMV4I |
| App opens but “Sync failed” | Same Wi‑Fi test as browser; fix server IP / `npm start` |
| No VS2008 available | Use **mobile.html** on gun until VM is set up |

---

## What I can help with next

- Build errors: paste the **full Error List** from VS2008.
- Merlin CPU/platform: tell us exact model (e.g. HTE00072) and CE version from **Settings → System**.
- NUR DLL missing on gun: F1/F2 + wedge still work; copy Nordic `NurApi` DLL per Merlin SDK if trigger inventory is silent.

---

## Quick reference — file map

| File | Purpose |
|------|---------|
| `handheld-ce/MerlinInventoryTest/*.cs` | App source |
| `handheld-ce/MerlinInventoryTest/MerlinInventoryTest.csproj` | Open this in VS2008 |
| `public/deploy/MerlinInventoryTest.cab` | Drop built CAB here |
| `scripts/publish-to-deploy.ps1` | Copies CAB to deploy folder |
| `http://<server>:3000/deploy/` | Install link on scanner |
