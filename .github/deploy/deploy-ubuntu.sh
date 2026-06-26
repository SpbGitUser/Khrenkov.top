#!/usr/bin/env bash
set -euo pipefail

PACKAGE_PATH="${1:?Release package path is required}"
APP_NAME="${APP_NAME:-khrenkov-top}"
DEPLOY_PATH="${DEPLOY_PATH:-/var/www/khrenkov-top}"
DOMAIN_NAME="${DOMAIN_NAME:-_}"
BACKEND_URLS="${BACKEND_URLS:-http://127.0.0.1:5000}"
BACKEND_PROXY_URL="${BACKEND_PROXY_URL:-http://127.0.0.1:5000}"
ENABLE_HTTPS="${ENABLE_HTTPS:-auto}"
LETSENCRYPT_EMAIL="${LETSENCRYPT_EMAIL:-}"

join_url() {
  local base="${1%/}"
  local path="${2#/}"
  printf '%s/%s' "${base}" "${path}"
}

show_backend_logs() {
  sudo systemctl status "${SERVICE_NAME}" --no-pager || true
  sudo journalctl -u "${SERVICE_NAME}" -n 120 --no-pager || true
}

wait_for_url() {
  local url="${1}"
  local label="${2}"
  local host_header="${3:-}"
  local attempt
  local curl_args=(-fsS --max-time 5)

  if [[ -n "${host_header}" ]]; then
    curl_args+=(-H "Host: ${host_header}")
  fi

  for attempt in {1..20}; do
    if curl "${curl_args[@]}" "${url}" >/dev/null 2>&1; then
      echo "${label} is healthy: ${url}"
      return 0
    fi

    sleep 2
  done

  echo "Timed out waiting for ${label}: ${url}" >&2
  curl -v --max-time 10 "${curl_args[@]}" "${url}" || true
  return 1
}

ensure_apt_packages() {
  local missing=()
  local package

  for package in "$@"; do
    if ! dpkg -s "${package}" >/dev/null 2>&1; then
      missing+=("${package}")
    fi
  done

  if ((${#missing[@]} > 0)); then
    sudo apt-get update
    sudo DEBIAN_FRONTEND=noninteractive apt-get install -y "${missing[@]}"
  fi
}

is_ip_address() {
  local value="${1}"
  [[ "${value}" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ || "${value}" == *:* ]]
}

https_requested() {
  case "${ENABLE_HTTPS,,}" in
    0|false|no|off|disabled)
      return 1
      ;;
  esac

  return 0
}

https_required() {
  case "${ENABLE_HTTPS,,}" in
    1|true|yes|on|required)
      return 0
      ;;
  esac

  return 1
}

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
test -x "${RELEASE_DIR}/backend/Khrenkov.top"
test -f "${RELEASE_DIR}/frontend/index.html"

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

ensure_apt_packages nginx curl ca-certificates

SSL_DOMAIN="${DOMAIN_NAME%% *}"
SSL_CERT="/etc/letsencrypt/live/${SSL_DOMAIN}/fullchain.pem"
SSL_KEY="/etc/letsencrypt/live/${SSL_DOMAIN}/privkey.pem"
SSL_OPTIONS="/etc/letsencrypt/options-ssl-nginx.conf"
SSL_DHPARAM="/etc/letsencrypt/ssl-dhparams.pem"
NGINX_SITE="/etc/nginx/sites-available/${APP_NAME}"

cert_available() {
  [[ "${SSL_DOMAIN}" != "_" ]] && sudo test -f "${SSL_CERT}" && sudo test -f "${SSL_KEY}"
}

https_can_be_managed() {
  [[ -n "${SSL_DOMAIN}" ]] && [[ "${SSL_DOMAIN}" != "_" ]] && ! is_ip_address "${SSL_DOMAIN}" && https_requested
}

write_nginx_locations() {
  cat <<EOF
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
EOF
}

write_nginx_config() {
  if cert_available; then
    {
      cat <<EOF
server {
    listen 80;
    server_name ${DOMAIN_NAME};

    return 301 https://\$host\$request_uri;
}

server {
    listen 443 ssl http2;
    server_name ${DOMAIN_NAME};

    ssl_certificate ${SSL_CERT};
    ssl_certificate_key ${SSL_KEY};
EOF
      if sudo test -f "${SSL_OPTIONS}"; then
        echo "    include ${SSL_OPTIONS};"
      fi
      if sudo test -f "${SSL_DHPARAM}"; then
        echo "    ssl_dhparam ${SSL_DHPARAM};"
      fi
      echo
      write_nginx_locations
      cat <<EOF
}
EOF
    } | sudo tee "${NGINX_SITE}" >/dev/null
  else
    {
      cat <<EOF
server {
    listen 80;
    server_name ${DOMAIN_NAME};

EOF
      write_nginx_locations
      cat <<EOF
}
EOF
    } | sudo tee "${NGINX_SITE}" >/dev/null
  fi
}

ensure_certificate() {
  if ! https_can_be_managed; then
    return 0
  fi

  ensure_apt_packages certbot python3-certbot-nginx

  local domain_args=()
  local name
  for name in ${DOMAIN_NAME}; do
    if [[ -n "${name}" ]] && [[ "${name}" != "_" ]] && ! is_ip_address "${name}"; then
      domain_args+=(-d "${name}")
    fi
  done

  if ((${#domain_args[@]} == 0)); then
    return 0
  fi

  local account_args=()
  if [[ -n "${LETSENCRYPT_EMAIL}" ]]; then
    account_args=(--email "${LETSENCRYPT_EMAIL}")
  else
    account_args=(--register-unsafely-without-email)
  fi

  if sudo certbot certonly --nginx --non-interactive --agree-tos --keep-until-expiring --cert-name "${SSL_DOMAIN}" "${account_args[@]}" "${domain_args[@]}"; then
    return 0
  fi

  if cert_available; then
    echo "Warning: certbot failed, but an existing certificate is present for ${SSL_DOMAIN}; keeping it." >&2
    return 0
  fi

  if https_required; then
    echo "HTTPS is required, but certbot could not issue a certificate for ${DOMAIN_NAME}." >&2
    return 1
  fi

  echo "Warning: certbot could not issue a certificate for ${DOMAIN_NAME}; continuing with HTTP." >&2
  return 0
}

write_nginx_config

sudo ln -sfn "/etc/nginx/sites-available/${APP_NAME}" "/etc/nginx/sites-enabled/${APP_NAME}"
sudo rm -f /etc/nginx/sites-enabled/default

sudo ln -sfn "${RELEASE_DIR}" "${CURRENT_DIR}"
sudo chown -R www-data:www-data "${DEPLOY_PATH}"

sudo systemctl daemon-reload
sudo systemctl enable "${SERVICE_NAME}"
sudo systemctl restart "${SERVICE_NAME}"
if ! sudo systemctl is-active --quiet "${SERVICE_NAME}"; then
  show_backend_logs
  exit 1
fi

BACKEND_HEALTH_URL="$(join_url "${BACKEND_PROXY_URL}" "/api/auth/status")"
if ! wait_for_url "${BACKEND_HEALTH_URL}" "backend"; then
  show_backend_logs
  exit 1
fi

sudo nginx -t
sudo systemctl enable --now nginx

if command -v ufw >/dev/null 2>&1 && sudo ufw status | grep -q '^Status: active'; then
  sudo ufw allow 80/tcp
  sudo ufw allow 443/tcp
fi

sudo systemctl reload nginx
NGINX_HEALTH_HOST=""
if [[ "${SSL_DOMAIN}" != "_" ]]; then
  NGINX_HEALTH_HOST="${SSL_DOMAIN}"
fi

if ! wait_for_url "http://127.0.0.1/" "nginx" "${NGINX_HEALTH_HOST}"; then
  sudo systemctl status nginx --no-pager || true
  sudo journalctl -u nginx -n 120 --no-pager || true
  sudo nginx -T || true
  exit 1
fi

if https_can_be_managed; then
  ensure_certificate

  if cert_available; then
    write_nginx_config
    sudo nginx -t
    sudo systemctl reload nginx
    curl -kfsS --max-time 10 -H "Host: ${SSL_DOMAIN}" "https://127.0.0.1/" >/dev/null || \
      echo "Warning: local HTTPS health check failed; public verification may show more details." >&2
  elif https_required; then
    echo "HTTPS is required, but no certificate is available for ${SSL_DOMAIN}." >&2
    exit 1
  fi
fi

sudo ss -ltnp | grep -E ':(80|443|5000)\b' || true

sudo find "${RELEASES_DIR}" -mindepth 1 -maxdepth 1 -type d | sort -r | tail -n +6 | xargs -r sudo rm -rf || \
  echo "Warning: old release cleanup failed" >&2
rm -f "${PACKAGE_PATH}" || echo "Warning: package cleanup failed: ${PACKAGE_PATH}" >&2

echo "Deploy completed on VPS"
