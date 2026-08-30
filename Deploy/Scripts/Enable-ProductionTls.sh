#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

domain="asterloom.kirayuukiasuna.cloud"
certbot_email="${CERTBOT_EMAIL:-admin@kirayuukiasuna.cloud}"

certbot certonly \
  --webroot \
  --webroot-path /var/www/letsencrypt \
  --domain "$domain" \
  --cert-name "$domain" \
  --email "$certbot_email" \
  --agree-tos \
  --non-interactive \
  --keep-until-expiring

install -m 0644 \
  Deploy/Nginx/asterloom.conf \
  /etc/nginx/sites-available/asterloom.kirayuukiasuna.cloud
nginx -t
systemctl reload nginx

echo "TLS enabled for https://$domain/."
