# Wi‑Fi updates for the Merlin (easiest paths)

## Recommended: inventory server HTTP (simplest)

Your Node server already serves `public/`. Drop updates in **`public/deploy/`**.

| Step | Action |
|------|--------|
| 1 | On PC: build `MerlinInventoryTest.cab` (see `handheld-ce/README-BUILD.md`) |
| 2 | `.\scripts\publish-to-deploy.ps1 -CabPath "...\MerlinInventoryTest.cab"` |
| 3 | On Merlin Wi‑Fi: open **`http://10.17.17.17:3000/deploy/`** (your server IP) |
| 4 | Tap **Wi‑Fi test** first (browser, no install) |
| 5 | Tap **Install native test app** when CAB link appears |
| 6 | Tap downloaded `.cab` → install to device |

**Why this beats NAS for day-to-day:** same IP you already use for inventory; no SMB passwords; works in CE browser; one folder to update.

---

## Browser-only test (zero install — do this today)

```
http://<server-ip>:3000/deploy/ce-wifi-test.html
```

Runs on the Merlin browser. Checks deploy info, heartbeat, and handheld sync. If all green, Wi‑Fi path is good before you build a CAB.

---

## NAS option (works, more friction)

Yes, you can use a NAS:

1. Create share e.g. `\\nas.local\inventory\deploy\`
2. Copy `MerlinInventoryTest.cab` there from your PC.
3. On Merlin: **File Explorer** → address bar → `\\nas.local\inventory\deploy`
4. Tap the `.cab` file.

**Caveats on Windows CE:**

- May need **domain/user/password** for SMB — CE File Explorer is picky.
- Some units need **CE 6 networking** + Wi‑Fi connected before UNC works.
- No auto “check for updates” unless you build that into the app later.

**Hybrid workflow:** build on PC → copy CAB to **both** NAS (archive) and `public/deploy/` (field install over HTTP).

---

## Other update methods

| Method | Effort | Best for |
|--------|--------|----------|
| **HTTP `/deploy/`** | Low | Routine updates on Wi‑Fi |
| **USB + WMDC** | Medium | First install, debugging |
| **microSD** | Low | No server/NAS on site |
| **NAS UNC** | Medium–High | IT already standardized on NAS |

---

## Future: in-app update

Native app can call `GET /api/deploy/info` — if `cab_available` and version &gt; installed, download `cab_url` and launch `wceload.exe`. The test app does not do this yet; the API is ready on the server.

---

## Files in this repo

| Path | Purpose |
|------|---------|
| `public/deploy/index.html` | Deploy hub on scanner browser |
| `public/deploy/ce-wifi-test.html` | No-install API test |
| `public/deploy/MerlinInventoryTest.cab` | You add after build (gitignored) |
| `handheld-ce/MerlinInventoryTest/` | C# CE source |
| `scripts/publish-to-deploy.ps1` | Copy CAB to deploy folder |

Add to `.gitignore`: `public/deploy/*.cab`
