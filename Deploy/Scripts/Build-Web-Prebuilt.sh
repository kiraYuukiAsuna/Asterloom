#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <repository-root> <output-tar.gz>" >&2
  exit 64
fi

repository_root=$(realpath "$1")
output_archive=$(realpath -m "$2")

if [[ ! -f "$repository_root/Frontend/package-lock.json" ]]; then
  echo "The repository root does not contain Frontend/package-lock.json." >&2
  exit 66
fi

build_directory=$(mktemp -d /tmp/asterloom-web-build.XXXXXX)
case "$build_directory" in
  /tmp/asterloom-web-build.*) ;;
  *) echo "Unexpected temporary build path." >&2; exit 70 ;;
esac

cleanup() {
  rm -rf -- "$build_directory"
}
trap cleanup EXIT

node_index=$(curl -fsSL https://nodejs.org/dist/latest-v24.x/)
node_archive=$(grep -oE 'node-v24[^" ]*-linux-x64\.tar\.xz' <<<"$node_index" | sort -u | head -n 1)
if [[ -z "$node_archive" ]]; then
  echo "Unable to discover the latest Node.js 24 Linux archive." >&2
  exit 69
fi

curl -fsSL "https://nodejs.org/dist/latest-v24.x/$node_archive" \
  -o "$build_directory/node.tar.xz"
curl -fsSL https://nodejs.org/dist/latest-v24.x/SHASUMS256.txt \
  -o "$build_directory/SHASUMS256.txt"

expected_hash=$(awk -v file="$node_archive" '$2 == file { print $1 }' \
  "$build_directory/SHASUMS256.txt")
actual_hash=$(sha256sum "$build_directory/node.tar.xz" | awk '{ print $1 }')
if [[ -z "$expected_hash" || "$actual_hash" != "$expected_hash" ]]; then
  echo "Node.js archive checksum verification failed." >&2
  exit 65
fi

tar -xJf "$build_directory/node.tar.xz" -C "$build_directory"
node_home="$build_directory/${node_archive%.tar.xz}"
export PATH="$node_home/bin:$PATH"
export ASTERLOOM_NEXT_STANDALONE=true
export NEXT_TELEMETRY_DISABLED=1

mkdir -p "$build_directory/Frontend" "$build_directory/Deploy"
rsync -a --delete \
  --exclude node_modules \
  --exclude .next \
  --exclude playwright-report \
  --exclude playwright-report-production \
  --exclude test-results \
  "$repository_root/Frontend/" "$build_directory/Frontend/"
cp "$repository_root/Deploy/Dockerfile.web-prebuilt" \
  "$build_directory/Deploy/Dockerfile.web-prebuilt"

(
  cd "$build_directory/Frontend"
  npm ci
  npm run build
)

mkdir -p "$(dirname "$output_archive")"
tar -czf "$output_archive" \
  -C "$build_directory" \
  Deploy/Dockerfile.web-prebuilt \
  Frontend/.next/standalone \
  Frontend/.next/static \
  Frontend/public

sha256sum "$output_archive"
du -h "$output_archive"
