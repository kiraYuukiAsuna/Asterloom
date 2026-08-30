#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <repository-root> <output-tar.gz>" >&2
  exit 64
fi

repository_root=$(realpath "$1")
output_archive=$(realpath -m "$2")

if [[ ! -f "$repository_root/Backend/Asterloom.Server/Asterloom.Server.csproj" ]]; then
  echo "The repository root does not contain Asterloom.Server." >&2
  exit 66
fi

build_directory=$(mktemp -d /tmp/asterloom-server-build.XXXXXX)
case "$build_directory" in
  /tmp/asterloom-server-build.*) ;;
  *) echo "Unexpected temporary build path." >&2; exit 70 ;;
esac

cleanup() {
  rm -rf -- "$build_directory"
}
trap cleanup EXIT

mkdir -p \
  "$build_directory/Backend/publish/server" \
  "$build_directory/Backend/publish/migrations" \
  "$build_directory/Deploy"

# Resolve the installed .NET 10 SDK inside the isolated build directory. The
# repository global.json may intentionally pin a newer feature band than the
# deployment workstation while the target framework remains compatible.
cd "$build_directory"

dotnet publish \
  "$repository_root/Backend/Asterloom.Server/Asterloom.Server.csproj" \
  --configuration Release \
  --output "$build_directory/Backend/publish/server" \
  /p:UseAppHost=false

dotnet publish \
  "$repository_root/Backend/Tools/Asterloom.Migrations/Asterloom.Migrations.csproj" \
  --configuration Release \
  --output "$build_directory/Backend/publish/migrations" \
  /p:UseAppHost=false

cp "$repository_root/Deploy/Dockerfile.server-prebuilt" \
  "$build_directory/Deploy/Dockerfile.server-prebuilt"

mkdir -p "$(dirname "$output_archive")"
tar -czf "$output_archive" \
  -C "$build_directory" \
  Deploy/Dockerfile.server-prebuilt \
  Backend/publish/server \
  Backend/publish/migrations

sha256sum "$output_archive"
du -h "$output_archive"
