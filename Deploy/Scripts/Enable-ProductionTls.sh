#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

# shellcheck disable=SC1091
source Deploy/Scripts/Production-Domain.sh
load_asterloom_production_domain false

certbot certonly \
  --webroot \
  --webroot-path /var/www/letsencrypt \
  --domain "$ASTERLOOM_DOMAIN" \
  --cert-name "$ASTERLOOM_DOMAIN" \
  --email "$CERTBOT_EMAIL" \
  --agree-tos \
  --non-interactive \
  --keep-until-expiring

bash Deploy/Scripts/Install-ProductionNginx.sh production

echo "TLS enabled for https://$ASTERLOOM_DOMAIN/."
