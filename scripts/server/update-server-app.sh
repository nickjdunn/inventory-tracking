#!/bin/sh
# Run on the server when not using the post-receive hook (manual deploy).
#   /opt/inventory-app/scripts/update-server-app.sh

set -e

ROOT="/opt/inventory-app"
GIT_DIR="$ROOT/repo.git"
WORK_TREE="$ROOT/app"
PM2_APP="rfid-brain"

echo "==> Checkout main -> $WORK_TREE"
git --git-dir="$GIT_DIR" --work-tree="$WORK_TREE" checkout -f main

echo "==> Latest commit:"
git --git-dir="$GIT_DIR" log -1 --oneline main

if [ -f "$WORK_TREE/public/deploy/index.html" ]; then
    echo "==> Deploy page CAB links:"
    grep -o 'Merlin[A-Za-z]*\.cab' "$WORK_TREE/public/deploy/index.html" | sort -u
fi

if command -v pm2 >/dev/null 2>&1; then
    echo "==> Restart $PM2_APP"
    pm2 restart "$PM2_APP"
else
    echo "==> pm2 not found"
fi

echo "==> Done"
