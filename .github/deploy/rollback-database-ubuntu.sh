#!/usr/bin/env bash
set -euo pipefail

# Run on the VPS with a target migration from the current release (or 0).
TARGET="${1:?Usage: bash rollback-database-ubuntu.sh <migration-name-or-id|0>}"
APP_NAME="${APP_NAME:-khrenkov-top}"
DEPLOY_PATH="${DEPLOY_PATH:-/var/www/khrenkov-top}"
BACKEND="${DEPLOY_PATH}/current/backend"
ENV_FILE="/etc/${APP_NAME}.env"

test -x "${BACKEND}/Khrenkov.top"
test -f "${ENV_FILE}"
sudo systemctl stop "${APP_NAME}.service"
sudo systemd-run --wait --pipe --collect \
  --property="User=www-data" \
  --property="WorkingDirectory=${BACKEND}" \
  --property="EnvironmentFile=${ENV_FILE}" \
  "${BACKEND}/Khrenkov.top" database rollback --to "${TARGET}"

echo "Database rollback completed. The backend remains stopped."
echo "Select a compatible release before starting it; deployment will apply migrations again."
