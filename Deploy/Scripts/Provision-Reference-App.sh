#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

domain="${ASTERLOOM_DOMAIN:-asterloom.kirayuukiasuna.cloud}"
base_url="https://$domain"
environment_file="${ASTERLOOM_ENV_FILE:-.env}"
reference_directory="${ASTERLOOM_REFERENCE_DIRECTORY:-.data/reference-app}"
reference_environment="$reference_directory/reference.env"
reference_state_directory="$reference_directory/state"
reference_app_uid="${ASTERLOOM_REFERENCE_APP_UID:-1654}"
service_client_id="${ASTERLOOM_REFERENCE_CLIENT_ID:-asterloom-reference-service}"
native_client_id="${ASTERLOOM_REFERENCE_INTERACTIVE_CLIENT_ID:-asterloom-reference-native}"
business_client_id="${ASTERLOOM_REFERENCE_IDENTITY_CLIENT_ID:-asterloom-reference-business}"
binding_id="7fc2e239-3fa1-7ef1-8c20-5c7d54b7bd77"

if [[ ! -f "$environment_file" ]]; then
  echo "Environment file not found: $environment_file" >&2
  exit 1
fi

set -a
# shellcheck disable=SC1090
source "$environment_file"
set +a

: "${ASTERLOOM_BOOTSTRAP_ADMIN_EMAIL:?ASTERLOOM_BOOTSTRAP_ADMIN_EMAIL is required}"
: "${ASTERLOOM_BOOTSTRAP_ADMIN_PASSWORD:?ASTERLOOM_BOOTSTRAP_ADMIN_PASSWORD is required}"
expose_confirmation_token="${ASTERLOOM_REFERENCE_EXPOSE_CONFIRMATION_TOKEN:-false}"
if [[ "$expose_confirmation_token" != "true" && "$expose_confirmation_token" != "false" ]]; then
  echo "ASTERLOOM_REFERENCE_EXPOSE_CONFIRMATION_TOKEN must be true or false." >&2
  exit 1
fi

temporary_directory="$(mktemp -d)"
trap 'rm -rf -- "$temporary_directory"' EXIT
cookie_jar="$temporary_directory/cookies.txt"
headers="$temporary_directory/headers.txt"
body="$temporary_directory/body"

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
  if [[ -z "$value" || "$value" == "null" ]]; then
    echo "Reference provisioning failed: missing $name." >&2
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
grep -q "登录成功" "$body"

curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --dump-header "$headers" \
  --output /dev/null \
  "$base_url$return_url"
callback_url="$(header_location)"
require_value "OIDC callback redirect" "$callback_url"
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --output /dev/null \
  "$callback_url"

session_file="$temporary_directory/session.json"
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --cookie-jar "$cookie_jar" \
  --output "$session_file" \
  "$base_url/api/auth/session"
csrf_token="$(jq --raw-output '.csrfToken' "$session_file")"
require_value "BFF CSRF token" "$csrf_token"

api_get() {
  local path="$1"
  local output="$2"
  curl --fail --silent --show-error \
    --cookie "$cookie_jar" \
    --cookie-jar "$cookie_jar" \
    --output "$output" \
    "$base_url/api/asterloom$path"
}

api_mutate() {
  local method="$1"
  local path="$2"
  local payload="$3"
  local output="$4"
  local status
  status="$(curl --silent --show-error \
    --request "$method" \
    --header "Content-Type: application/json" \
    --header "Origin: $base_url" \
    --header "x-csrf-token: $csrf_token" \
    --cookie "$cookie_jar" \
    --cookie-jar "$cookie_jar" \
    --data "$payload" \
    --output "$output" \
    --write-out '%{http_code}' \
    "$base_url/api/asterloom$path")"
  if [[ "$status" -lt 200 || "$status" -ge 300 ]]; then
    echo "Reference provisioning failed: $method $path returned HTTP $status." >&2
    jq . "$output" >&2 2>/dev/null || sed -n '1,20p' "$output" >&2
    exit 1
  fi
}

tenants_file="$temporary_directory/tenants.json"
api_get "/api/v1/tenants?pageSize=100&query=asterloom-reference-identity" "$tenants_file"
identity_tenant_id="$(jq --raw-output \
  '.tenants[]? | select(.slug == "asterloom-reference-identity") | .id' \
  "$tenants_file" | head -n 1)"
if [[ -z "$identity_tenant_id" ]]; then
  identity_tenant_file="$temporary_directory/identity-tenant.json"
  api_mutate POST "/api/v1/tenants" \
    '{"slug":"asterloom-reference-identity","displayName":"Asterloom Reference Identity"}' \
    "$identity_tenant_file"
  identity_tenant_id="$(jq --raw-output '.id' "$identity_tenant_file")"
fi
require_value "reference Identity tenant" "$identity_tenant_id"

applications_file="$temporary_directory/applications.json"
api_get "/api/v1/tenants/$identity_tenant_id/applications?pageSize=100&query=passport-demo" \
  "$applications_file"
identity_application_id="$(jq --raw-output \
  '.applications[]? | select(.slug == "passport-demo") | .id' \
  "$applications_file" | head -n 1)"
if [[ -z "$identity_application_id" ]]; then
  identity_application_file="$temporary_directory/identity-application.json"
  api_mutate POST "/api/v1/tenants/$identity_tenant_id/applications" \
    '{"slug":"passport-demo","displayName":"Passport Business Integration Demo"}' \
    "$identity_application_file"
  identity_application_id="$(jq --raw-output '.id' "$identity_application_file")"
fi
require_value "reference Identity application" "$identity_application_id"

clients_file="$temporary_directory/clients.json"
api_get "/api/v1/identity/clients?pageSize=100&query=$service_client_id" "$clients_file"
service_version="$(jq --raw-output --arg id "$service_client_id" \
  '.clients[]? | select(.clientId == $id) | .version' "$clients_file" | head -n 1)"
service_secret=""
if [[ -n "$service_version" ]]; then
  rotate_file="$temporary_directory/service-rotate.json"
  api_mutate POST "/api/v1/identity/clients/$service_client_id:rotate-secret" \
    "$(jq -cn --arg version "$service_version" '{expectedVersion:$version}')" \
    "$rotate_file"
  service_secret="$(jq --raw-output '.clientSecret' "$rotate_file")"
else
  create_file="$temporary_directory/service-create.json"
  api_mutate POST "/api/v1/identity/clients" \
    "$(jq -cn --arg id "$service_client_id" '{
      clientId:$id,
      displayName:"Asterloom reference service",
      applicationType:"OIDC_APPLICATION_TYPE_WEB",
      clientType:"OIDC_CLIENT_TYPE_CONFIDENTIAL",
      grantTypes:["OIDC_GRANT_TYPE_CLIENT_CREDENTIALS"],
      scopes:["asterloom.api"]
    }')" \
    "$create_file"
  service_secret="$(jq --raw-output '.clientSecret' "$create_file")"
fi
require_value "reference service client secret" "$service_secret"

api_get "/api/v1/identity/clients?pageSize=100&query=$native_client_id" "$clients_file"
native_exists="$(jq --arg id "$native_client_id" '[.clients[]? | select(.clientId == $id)] | length' "$clients_file")"
if [[ "$native_exists" == "0" ]]; then
  native_file="$temporary_directory/native-create.json"
  api_mutate POST "/api/v1/identity/clients" \
    "$(jq -cn --arg id "$native_client_id" '{
      clientId:$id,
      displayName:"Asterloom reference native client",
      applicationType:"OIDC_APPLICATION_TYPE_NATIVE",
      clientType:"OIDC_CLIENT_TYPE_PUBLIC",
      grantTypes:["OIDC_GRANT_TYPE_AUTHORIZATION_CODE","OIDC_GRANT_TYPE_REFRESH_TOKEN"],
      redirectUris:["http://localhost/"],
      postLogoutRedirectUris:["http://localhost/"],
      scopes:["asterloom.api"]
    }')" \
    "$native_file"
fi

api_get "/api/v1/identity/clients?pageSize=100&query=$business_client_id" "$clients_file"
business_version="$(jq --raw-output --arg id "$business_client_id" \
  '.clients[]? | select(.clientId == $id) | .version' "$clients_file" | head -n 1)"
business_secret=""
business_payload="$(jq -cn \
  --arg tenant "$identity_tenant_id" \
  --arg application "$identity_application_id" \
  '{
    displayName:"Asterloom reference business backend",
    grantTypes:[
      "OIDC_GRANT_TYPE_CLIENT_CREDENTIALS",
      "OIDC_GRANT_TYPE_PASSWORD",
      "OIDC_GRANT_TYPE_REFRESH_TOKEN"
    ],
    redirectUris:[],
    postLogoutRedirectUris:[],
    scopes:["asterloom.api","openid","profile","email","roles","offline_access"],
    tenantId:$tenant,
    applicationId:$application,
    allowUserRegistration:true,
    allowMembershipAutoJoin:true
  }')"
if [[ -n "$business_version" ]]; then
  business_update_file="$temporary_directory/business-update.json"
  api_mutate PATCH "/api/v1/identity/clients/$business_client_id" \
    "$(jq --arg version "$business_version" '. + {expectedVersion:$version}' \
      <<<"$business_payload")" \
    "$business_update_file"
  business_version="$(jq --raw-output '.version' "$business_update_file")"
  business_rotate_file="$temporary_directory/business-rotate.json"
  api_mutate POST "/api/v1/identity/clients/$business_client_id:rotate-secret" \
    "$(jq -cn --arg version "$business_version" '{expectedVersion:$version}')" \
    "$business_rotate_file"
  business_secret="$(jq --raw-output '.clientSecret' "$business_rotate_file")"
else
  business_create_file="$temporary_directory/business-create.json"
  api_mutate POST "/api/v1/identity/clients" \
    "$(jq --arg id "$business_client_id" \
      '. + {clientId:$id,applicationType:"OIDC_APPLICATION_TYPE_WEB",clientType:"OIDC_CLIENT_TYPE_CONFIDENTIAL"}' \
      <<<"$business_payload")" \
    "$business_create_file"
  business_secret="$(jq --raw-output '.clientSecret' "$business_create_file")"
fi
require_value "reference business client secret" "$business_secret"

roles_file="$temporary_directory/roles.json"
api_get "/api/v1/authorization/roles?pageSize=100&query=super-administrator" "$roles_file"
super_role_id="$(jq --raw-output '.roles[]? | select(.key == "super-administrator") | .id' \
  "$roles_file" | head -n 1)"
require_value "super-administrator role" "$super_role_id"

bindings_file="$temporary_directory/bindings.json"
api_get "/api/v1/authorization/role-bindings?pageSize=100&actorId=$service_client_id" \
  "$bindings_file"
binding_exists="$(jq --arg actor "$service_client_id" \
  '[.roleBindings[]? | select(.actorId == $actor and .roleKey == "super-administrator")] | length' \
  "$bindings_file")"
if [[ "$binding_exists" == "0" ]]; then
  binding_file="$temporary_directory/binding-create.json"
  api_mutate PUT "/api/v1/authorization/role-bindings/$binding_id" \
    "$(jq -cn \
      --arg actor "$service_client_id" \
      --arg role "$super_role_id" \
      '{actorId:$actor,roleId:$role,scope:{},expectedVersion:0}')" \
    "$binding_file"
fi

mkdir -p "$reference_directory"
chmod 700 "$reference_directory"
if [[ ! "$reference_app_uid" =~ ^[0-9]+$ ]]; then
  echo "ASTERLOOM_REFERENCE_APP_UID must be a numeric user identifier." >&2
  exit 1
fi
mkdir -p "$reference_state_directory"
chown "$reference_app_uid:$reference_app_uid" "$reference_state_directory"
chmod 700 "$reference_state_directory"
umask 077
{
  printf 'ASTERLOOM_REFERENCE_CLIENT_ID=%s\n' "$service_client_id"
  printf 'ASTERLOOM_REFERENCE_CLIENT_SECRET=%s\n' "$service_secret"
  printf 'ASTERLOOM_REFERENCE_INTERACTIVE_CLIENT_ID=%s\n' "$native_client_id"
  printf 'ASTERLOOM_REFERENCE_IDENTITY_CLIENT_ID=%s\n' "$business_client_id"
  printf 'ASTERLOOM_REFERENCE_IDENTITY_CLIENT_SECRET=%s\n' "$business_secret"
  printf 'Asterloom__Identity__Enabled=true\n'
  printf 'Asterloom__Identity__BaseAddress=%s\n' "$base_url/"
  printf 'Asterloom__Identity__Issuer=%s\n' "$base_url/"
  printf 'Asterloom__Identity__ClientId=%s\n' "$business_client_id"
  printf 'Asterloom__Identity__ClientSecret=%s\n' "$business_secret"
  printf 'Asterloom__Identity__AllowInsecureHttpForDevelopment=false\n'
  printf 'Asterloom__Identity__ExposeEmailVerificationToken=%s\n' \
    "$expose_confirmation_token"
} > "$reference_environment"
chmod 600 "$reference_environment"

echo "Reference OIDC clients, business application binding, and global authorization binding are ready."
echo "Credentials were written to $reference_environment with mode 0600."
echo "Recreate reference-backend after provisioning so it reloads the rotated business client secret."
