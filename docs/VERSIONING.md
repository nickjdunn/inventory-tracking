# Version numbers and updates

## Format

**`MAJOR.MINOR.COMMIT_COUNT+gitHash`** — example: `1.0.46+96c8284`

| Part | Source |
|------|--------|
| MAJOR.MINOR | `version.json` (you bump for big releases) |
| COMMIT_COUNT | `git rev-list --count HEAD` (auto, increases every commit) |
| gitHash | Short commit id (traceability) |

.NET assembly uses four parts: `1.0.46.0` (same numbers, no hash).

## Sync before run or publish

```bash
npm run version:sync   # or happens automatically on npm start
```

Updates:

- `version.generated.json` (repo root + `public/`)
- `AppConfig.cs` / `AssemblyInfo.cs` (native app)
- Server reads `version.generated.json` for `/api/deploy/info` and `/api/ping`

## Where version appears

| Place | What you see |
|-------|----------------|
| Native app title + Settings | `App v1.0.46+96c8284` |
| Deploy hub (`/deploy/`) | Web client + server version; update banner |
| Mobile UI | Footer line + update banner |
| Wi‑Fi test page | Top line |
| Server API | `GET /api/deploy/info`, `GET /api/ping` |

## Updates

**Native app:** On startup and via Settings → **Check updates**. If server version is newer, you get a prompt and link to `/deploy/` to install the `.cab`.

**Browser on gun:** Deploy hub and mobile UI check on load. Yellow banner when server is ahead; install CAB or refresh the page.

**Publishing a new CAB:**

1. `node scripts/sync-version.js` (bumps version from latest commit)
2. Build in VS2008
3. `.\scripts\publish-to-deploy.ps1 -CabPath "...\MerlinInventoryTest.cab"`

After server restart, guns with an older build should see the update prompt.
