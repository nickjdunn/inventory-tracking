#!/bin/sh
# Pull latest main from GitHub into bare repo, update app/, restart pm2.
# Run on server when PC "git push prod" is not set up:
#   bash /opt/inventory-app/app/scripts/server/pull-from-github.sh

set -e

ROOT="/opt/inventory-app"
GIT_DIR="$ROOT/repo.git"
WORK_TREE="$ROOT/app"
PM2_APP="rfid-brain"
GITHUB="https://github.com/nickjdunn/inventory-tracking.git"

echo "==> Fetch main from GitHub into repo.git"
git --git-dir="$GIT_DIR" fetch "$GITHUB" main:main

echo "==> Checkout into app/"
git --git-dir="$GIT_DIR" --work-tree="$WORK_TREE" checkout -f main

echo "==> Latest:"
git --git-dir="$GIT_DIR" log -1 --oneline main

if command -v pm2 >/dev/null 2>&1; then
    pm2 restart "$PM2_APP"
fi

echo "==> Done. CAB files still need scp if not on server."
