# Server deploy (bare repo + work tree)

Your Proxmox box uses a **bare git repo** and a separate **app folder**. A normal `cd app && git pull` does not apply.

| Path | Purpose |
|------|---------|
| `/opt/inventory-app/repo.git` | Bare repository (receives pushes) |
| `/opt/inventory-app/app` | Running code (`pm2` / `rfid-brain`) |
| `/opt/inventory-app/app/public/deploy/` | CAB downloads for the gun |

## One-time setup on server

SSH in as root, then install the post-receive hook (auto-deploy on push):

```bash
ssh root@10.17.17.17

cat > /opt/inventory-app/repo.git/hooks/post-receive << 'EOF'
#!/bin/sh
set -e
GIT_DIR="/opt/inventory-app/repo.git"
WORK_TREE="/opt/inventory-app/app"
PM2_APP="rfid-brain"
while read oldrev newrev refname; do
    branch="${refname#refs/heads/}"
    [ "$branch" = "main" ] || continue
    git --git-dir="$GIT_DIR" --work-tree="$WORK_TREE" checkout -f main
    command -v pm2 >/dev/null && pm2 restart "$PM2_APP" || true
    echo "deployed $newrev"
done
EOF

chmod +x /opt/inventory-app/repo.git/hooks/post-receive
```

Or copy from repo after pull:

```bash
cp /opt/inventory-app/app/scripts/server/post-receive /opt/inventory-app/repo.git/hooks/post-receive
chmod +x /opt/inventory-app/repo.git/hooks/post-receive
```

## Every deploy from your PC

```powershell
cd "C:\Users\nickj\Documents\Coding Projects\inventory-tracking\inventory-tracking"

# Push code (triggers hook if installed)
.\scripts\push-to-prod.ps1
```

Or manually:

```powershell
git push origin main
git push prod main

scp "public\deploy\MerlinInventoryTest.cab" root@10.17.17.17:/opt/inventory-app/app/public/deploy/
scp "public\deploy\MerlinDeviceAudit.cab" root@10.17.17.17:/opt/inventory-app/app/public/deploy/
```

## Manual deploy on server (no hook)

```bash
cd /opt/inventory-app
git --git-dir=repo.git --work-tree=app checkout -f main
pm2 restart rfid-brain
```

Or:

```bash
bash /opt/inventory-app/app/scripts/server/update-server-app.sh
```

## Verify

```bash
git --git-dir=/opt/inventory-app/repo.git log -1 --oneline main
grep MerlinDeviceAudit.cab /opt/inventory-app/app/public/deploy/index.html
ls -la /opt/inventory-app/app/public/deploy/*.cab
curl -I http://127.0.0.1:3000/deploy/MerlinDeviceAudit.cab
```

Gun browser: `http://10.17.17.17:3000/deploy/`
