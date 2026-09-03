#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

# shellcheck disable=SC1091
source Deploy/Scripts/Production-Domain.sh
load_asterloom_production_domain false

umask 077
install -d -m 0700 Deploy/Secrets
install -d -m 0700 .data/dataprotection-keys
chown -R 1654:1654 .data/dataprotection-keys

if [[ ! -f "$ASTERLOOM_ENV_FILE" ]]; then
  postgres_password="$(openssl rand -hex 24)"
  minio_password="$(openssl rand -hex 24)"
  redis_password="$(openssl rand -hex 24)"
  admin_password="Ast!$(openssl rand -hex 18)Z9"
  oidc_secret="$(openssl rand -hex 32)"
  telemetry_ingestion_key="$(openssl rand -hex 32)"
  session_key="$(openssl rand -base64 32 | tr -d '\n')"
  certificate_password="$(openssl rand -hex 24)"

  printf '%s\n' \
    "ASTERLOOM_DOMAIN=$ASTERLOOM_DOMAIN" \
    "CERTBOT_EMAIL=$CERTBOT_EMAIL" \
    "POSTGRES_PASSWORD=$postgres_password" \
    "MINIO_ROOT_USER=asterloom-prod" \
    "MINIO_ROOT_PASSWORD=$minio_password" \
    "REDIS_PASSWORD=$redis_password" \
    "ASTERLOOM_BOOTSTRAP_ADMIN_NAME=Asterloom-Administrator" \
    "ASTERLOOM_BOOTSTRAP_ADMIN_EMAIL=admin@$ASTERLOOM_DOMAIN" \
    "ASTERLOOM_BOOTSTRAP_ADMIN_PASSWORD=$admin_password" \
    "ASTERLOOM_OIDC_CLIENT_SECRET=$oidc_secret" \
    "TELEMETRY_INGESTION_API_KEY=$telemetry_ingestion_key" \
    "ASTERLOOM_SESSION_ENCRYPTION_KEY=$session_key" \
    "ASTERLOOM_CERTIFICATE_PASSWORD=$certificate_password" > "$ASTERLOOM_ENV_FILE"
  chmod 0600 "$ASTERLOOM_ENV_FILE"
fi

if ! grep -q '^TELEMETRY_INGESTION_API_KEY=' "$ASTERLOOM_ENV_FILE"; then
  printf 'TELEMETRY_INGESTION_API_KEY=%s\n' "$(openssl rand -hex 32)" \
    >> "$ASTERLOOM_ENV_FILE"
fi

set -a
# shellcheck disable=SC1091
source "$ASTERLOOM_ENV_FILE"
set +a

generate_certificate() {
  local purpose="$1"
  local common_name="$2"
  local key_usage="$3"
  local output="$4"
  local temporary_directory
  temporary_directory="$(mktemp -d)"
  openssl req -x509 -newkey rsa:3072 -sha256 -days 825 -nodes \
    -subj "/CN=$common_name" \
    -addext "keyUsage=critical,$key_usage" \
    -keyout "$temporary_directory/$purpose.key" \
    -out "$temporary_directory/$purpose.crt" >/dev/null 2>&1
  openssl pkcs12 -export \
    -inkey "$temporary_directory/$purpose.key" \
    -in "$temporary_directory/$purpose.crt" \
    -name "$common_name" \
    -out "$output" \
    -passout "pass:$ASTERLOOM_CERTIFICATE_PASSWORD" >/dev/null 2>&1
  chmod 0600 "$output"
  rm -f -- \
    "$temporary_directory/$purpose.key" \
    "$temporary_directory/$purpose.crt"
  rmdir -- "$temporary_directory"
}

if [[ ! -f Deploy/Secrets/asterloom-signing.pfx ]]; then
  generate_certificate \
    signing \
    "Asterloom Token Signing" \
    digitalSignature \
    Deploy/Secrets/asterloom-signing.pfx
fi

if [[ ! -f Deploy/Secrets/asterloom-encryption.pfx ]]; then
  generate_certificate \
    encryption \
    "Asterloom Token Encryption" \
    keyEncipherment,dataEncipherment \
    Deploy/Secrets/asterloom-encryption.pfx
fi

chown 1654:1654 \
  Deploy/Secrets/asterloom-signing.pfx \
  Deploy/Secrets/asterloom-encryption.pfx
chmod 0400 \
  Deploy/Secrets/asterloom-signing.pfx \
  Deploy/Secrets/asterloom-encryption.pfx

install -d -m 0755 /var/www/letsencrypt
bash Deploy/Scripts/Install-ProductionNginx.sh bootstrap

echo "Production host prerequisites prepared for $ASTERLOOM_DOMAIN."
