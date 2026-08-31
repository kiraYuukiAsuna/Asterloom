#!/usr/bin/env bash

# Shared production-domain loading for deployment scripts. This file is meant
# to be sourced, not executed directly.
ASTERLOOM_DEFAULT_DOMAIN="asterloom.momiya.cloud"

load_asterloom_production_domain() {
  local require_environment_file="${1:-false}"
  local environment_file="${ASTERLOOM_ENV_FILE:-.env}"
  local explicit_domain="${ASTERLOOM_DOMAIN:-}"
  local explicit_certbot_email="${CERTBOT_EMAIL:-}"

  if [[ -f "$environment_file" ]]; then
    set -a
    # shellcheck disable=SC1090
    source "$environment_file"
    set +a
  elif [[ "$require_environment_file" == "true" ]]; then
    echo "Environment file not found: $environment_file" >&2
    return 1
  fi

  ASTERLOOM_ENV_FILE="$environment_file"
  ASTERLOOM_DOMAIN="${explicit_domain:-${ASTERLOOM_DOMAIN:-$ASTERLOOM_DEFAULT_DOMAIN}}"
  ASTERLOOM_DOMAIN="${ASTERLOOM_DOMAIN,,}"
  CERTBOT_EMAIL="${explicit_certbot_email:-${CERTBOT_EMAIL:-admin@$ASTERLOOM_DOMAIN}}"

  if [[ ${#ASTERLOOM_DOMAIN} -gt 253 ||
        ! "$ASTERLOOM_DOMAIN" =~ ^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$ ]]; then
    echo "Invalid ASTERLOOM_DOMAIN: $ASTERLOOM_DOMAIN" >&2
    return 1
  fi

  if [[ ! "$CERTBOT_EMAIL" =~ ^[^[:space:]@]+@[^[:space:]@]+$ ]]; then
    echo "Invalid CERTBOT_EMAIL: $CERTBOT_EMAIL" >&2
    return 1
  fi

  export ASTERLOOM_ENV_FILE ASTERLOOM_DOMAIN CERTBOT_EMAIL
}
