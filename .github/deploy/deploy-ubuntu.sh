#!/usr/bin/env bash
set -euo pipefail

PACKAGE_PATH="${1:?Release package path is required}"
APP_NAME="${APP_NAME:-khrenkov-top}"
DEPLOY_PATH="${DEPLOY_PATH:-/var/www/khrenkov-top}"
DOMAIN_NAME="${DOMAIN_NAME:-_}"
BACKEND_URLS="${BACKEND_URLS:-http://127.0.0.1:5000}"
BACKEND_PROXY_URL="${BACKEND_PROXY_URL:-http://127.0.0.1:5000}"

if [[ -n "${SITE_PASSWORD_B64:-}" ]]; then
  SITE_PASSWORD="$(printf '%s' "${SITE_PASSWORD_B64}" | base64 -d)"
else
  SITE_PASSWORD="${SITE_PASSWORD:?SITE_PASSWORD is required}"
fi

SERVICE_NAME="${APP_NAME}.service"
ENV_FILE="/etc/${APP_NAME}.env"
RELEASE_ID="$(date +%Y%m%d%H%M%S)"
RELEASES_DIR="${DEPLOY_PATH}/releases"
RELEASE_DIR="${RELEASES_DIR}/${RELEASE_ID}"
CURRENT_DIR="${DEPLOY_PATH}/current"
SHARED_DIR="${DEPLOY_PATH}/shared"
UPLOADS_DIR="${SHARED_DIR}/Uploads"

sudo mkdir -p "${RELEASE_DIR}" "${UPLOADS_DIR}"
sudo tar --no-same-owner -xzf "${PACKAGE_PATH}" -C "${RELEASE_DIR}"

sudo rm -rf "${RELEASE_DIR}/backend/Uploads"
sudo ln -sfn "${UPLOADS_DIR}" "${RELEASE_DIR}/backend/Uploads"
sudo chmod +x "${RELEASE_DIR}/backend/Khrenkov.top"

sudo tee "${ENV_FILE}" >/dev/null <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=${BACKEND_URLS}
SitePassword=${SITE_PASSWORD}
EOF
sudo chmod 600 "${ENV_FILE}"

sudo tee "/etc/systemd/system/${SERVICE_NAME}" >/dev/null <<EOF
[Unit]
Description=Khrenkov.top backend
After=network.target

[Service]
WorkingDirectory=${CURRENT_DIR}/backend
ExecStart=${CURRENT_DIR}/backend/Khrenkov.top
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=${APP_NAME}
User=www-data
EnvironmentFile=${ENV_FILE}

[Install]
WantedBy=multi-user.target
EOF

if ! command -v nginx >/dev/null 2>&1; then
  sudo apt-get update
  sudo apt-get install -y nginx
fi

sudo tee "/etc/nginx/sites-available/${APP_NAME}" >/dev/null <<EOF
server {
    listen 80;
    server_name ${DOMAIN_NAME};

    root ${CURRENT_DIR}/frontend;
    index index.html;

    client_max_body_size 512m;

    location /api/ {
        proxy_pass ${BACKEND_PROXY_URL};
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location /share/ {
        proxy_pass ${BACKEND_PROXY_URL};
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location / {
        try_files \$uri \$uri/ /index.html;
    }
}
EOF

sudo ln -sfn "/etc/nginx/sites-available/${APP_NAME}" "/etc/nginx/sites-enabled/${APP_NAME}"
sudo rm -f /etc/nginx/sites-enabled/default

sudo ln -sfn "${RELEASE_DIR}" "${CURRENT_DIR}"
sudo chown -R www-data:www-data "${DEPLOY_PATH}"

sudo systemctl daemon-reload
sudo systemctl enable "${SERVICE_NAME}"
sudo systemctl restart "${SERVICE_NAME}"
sudo nginx -t
sudo systemctl reload nginx

find "${RELEASES_DIR}" -mindepth 1 -maxdepth 1 -type d | sort -r | tail -n +6 | xargs -r sudo rm -rf
rm -f "${PACKAGE_PATH}"
