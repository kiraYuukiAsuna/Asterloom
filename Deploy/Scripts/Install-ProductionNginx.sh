#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <bootstrap|production> [--render]" >&2
  exit 64
fi

mode="$1"
render_only="${2:-}"
if [[ -n "$render_only" && "$render_only" != "--render" ]]; then
  echo "Unknown option: $render_only" >&2
  exit 64
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

# shellcheck disable=SC1091
source Deploy/Scripts/Production-Domain.sh
load_asterloom_production_domain false

case "$mode" in
  bootstrap)
    template="Deploy/Nginx/asterloom.bootstrap.conf"
    ;;
  production)
    template="Deploy/Nginx/asterloom.conf"
    ;;
  *)
    echo "Unknown Nginx configuration mode: $mode" >&2
    exit 64
    ;;
esac

render_configuration() {
  sed "s/__ASTERLOOM_DOMAIN__/$ASTERLOOM_DOMAIN/g" "$template"
}

if [[ "$render_only" == "--render" ]]; then
  render_configuration
  exit 0
fi

rendered_configuration="$(mktemp)"
trap 'rm -f -- "$rendered_configuration"' EXIT
render_configuration > "$rendered_configuration"

if grep -q '__ASTERLOOM_DOMAIN__' "$rendered_configuration"; then
  echo "Nginx template still contains an unresolved domain placeholder." >&2
  exit 65
fi

install -m 0644 "$rendered_configuration" /etc/nginx/sites-available/asterloom

# Older revisions used a domain-specific Asterloom site name. Remove only
# enabled symlinks that resolve to an Asterloom-prefixed file in sites-available.
for legacy_site in /etc/nginx/sites-enabled/asterloom.*; do
  [[ -L "$legacy_site" ]] || continue
  resolved_target="$(readlink -f "$legacy_site")"
  case "$resolved_target" in
    /etc/nginx/sites-available/asterloom.*)
      rm -f -- "$legacy_site"
      ;;
  esac
done

ln -sfn /etc/nginx/sites-available/asterloom /etc/nginx/sites-enabled/asterloom
nginx -t
systemctl reload nginx

echo "Installed $mode Nginx configuration for $ASTERLOOM_DOMAIN."
