#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

# shellcheck disable=SC1091
source Deploy/Scripts/Production-Domain.sh
load_asterloom_production_domain true

domain="$ASTERLOOM_DOMAIN"
base_url="https://$domain"

: "${ASTERLOOM_BOOTSTRAP_ADMIN_EMAIL:?ASTERLOOM_BOOTSTRAP_ADMIN_EMAIL is required}"
: "${ASTERLOOM_BOOTSTRAP_ADMIN_PASSWORD:?ASTERLOOM_BOOTSTRAP_ADMIN_PASSWORD is required}"

temporary_directory="$(mktemp -d)"
trap 'rm -rf -- "$temporary_directory"' EXIT

cookie_jar="$temporary_directory/cookies.txt"
headers="$temporary_directory/headers.txt"
body="$temporary_directory/body.html"

header_location() {
  awk '
    BEGIN { IGNORECASE = 1 }
    /^location:/ {
      sub(/\r$/, "")
      sub(/^[^:]+:[[:space:]]*/, "")
      location = $0
    }
    END { print location }
  ' "$headers"
}

require_value() {
  local name="$1"
  local value="$2"
  if [[ -z "$value" ]]; then
    echo "Smoke test failed: missing $name." >&2
    exit 1
  fi
}

curl --fail --silent --show-error \
  --cookie-jar "$cookie_jar" \
  --dump-header "$headers" \
  --output /dev/null \
  "$base_url/api/auth/login"

authorization_url="$(header_location)"
require_value "authorization redirect" "$authorization_url"
if [[ "$authorization_url" != "$base_url/connect/authorize?"* ]]; then
  echo "Smoke test failed: unexpected authorization redirect." >&2
  exit 1
fi

curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --location \
  --max-redirs 5 \
  --output "$body" \
  "$authorization_url"

antiforgery_token="$(sed -n 's/.*name="__RequestVerificationToken" value="\([^"]*\)".*/\1/p' "$body" | head -n 1)"
return_url="$(sed -n 's/.*name="ReturnUrl" value="\([^"]*\)".*/\1/p' "$body" | head -n 1)"
return_url="$(printf '%s' "$return_url" | sed 's/&amp;/\&/g')"
require_value "anti-forgery token" "$antiforgery_token"
require_value "Passport return URL" "$return_url"

curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --data-urlencode "__RequestVerificationToken=$antiforgery_token" \
  --data-urlencode "ReturnUrl=$return_url" \
  --data-urlencode "Email=$ASTERLOOM_BOOTSTRAP_ADMIN_EMAIL" \
  --data-urlencode "Password=$ASTERLOOM_BOOTSTRAP_ADMIN_PASSWORD" \
  --data-urlencode "RememberMe=false" \
  --output "$body" \
  "$base_url/passport/login"

if ! grep -Eq "登录成功|Signed in" "$body"; then
  echo "Smoke test failed: Passport rejected the bootstrap administrator." >&2
  exit 1
fi

curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --dump-header "$headers" \
  --output /dev/null \
  "$base_url$return_url"

callback_url="$(header_location)"
require_value "OIDC callback redirect" "$callback_url"
if [[ "$callback_url" != "$base_url/api/auth/callback?"* ]]; then
  echo "Smoke test failed: unexpected OIDC callback redirect." >&2
  exit 1
fi

curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --dump-header "$headers" \
  --output /dev/null \
  "$callback_url"

callback_result_url="$(header_location)"
require_value "callback result redirect" "$callback_result_url"
if [[ "$callback_result_url" != "$base_url/" ]]; then
  callback_result_path="${callback_result_url#"$base_url"}"
  echo "Smoke test failed: callback redirected to $callback_result_path." >&2
  exit 1
fi

dashboard_status="$(curl --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --output "$body" \
  --write-out '%{http_code}' \
  "$base_url/")"
if [[ "$dashboard_status" != "200" ]]; then
  echo "Smoke test failed: authenticated dashboard returned HTTP $dashboard_status." >&2
  exit 1
fi

session_file="$temporary_directory/session.json"
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --output "$session_file" \
  "$base_url/api/auth/session"
jq --exit-status '.authenticated == true and (.csrfToken | length > 0)' \
  "$session_file" >/dev/null

api_file="$temporary_directory/api.json"
api_status="$(curl --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --output "$api_file" \
  --write-out '%{http_code}' \
  "$base_url/api/asterloom/api/v1/identity/users")"
if [[ "$api_status" != "200" ]]; then
  echo "Smoke test failed: authenticated JSON API returned HTTP $api_status." >&2
  exit 1
fi
jq --exit-status . "$api_file" >/dev/null

csrf_token="$(jq --raw-output '.csrfToken' "$session_file")"
logout_file="$temporary_directory/logout.json"
logout_status="$(curl --silent --show-error \
  --request POST \
  --header "Origin: $base_url" \
  --header "x-csrf-token: $csrf_token" \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --output "$logout_file" \
  --write-out '%{http_code}' \
  "$base_url/api/auth/logout")"
if [[ "$logout_status" != "200" ]]; then
  echo "Smoke test failed: logout returned HTTP $logout_status." >&2
  exit 1
fi

logout_url="$(jq --raw-output '.logoutUrl' "$logout_file")"
require_value "OIDC logout URL" "$logout_url"
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --location \
  --max-redirs 5 \
  --output /dev/null \
  "$logout_url"

echo "Production smoke test passed: HTTPS, Passport, OIDC, BFF session, JSON API, and logout."
