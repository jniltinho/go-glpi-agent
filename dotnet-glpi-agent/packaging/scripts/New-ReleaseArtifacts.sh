#!/usr/bin/env bash
# Produce checksums, SBOM snapshot, and notices for packaging artifacts.
set -euo pipefail

ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
VERSION=${VERSION:-0.1.0}
MSI_DIR="$ROOT/artifacts/msi"
PUBLISH_DIR="$ROOT/artifacts/publish/win-x64"
OUT_DIR="${OUT_DIR:-$ROOT/artifacts/release/$VERSION}"

mkdir -p "$OUT_DIR"

if [[ -d "$MSI_DIR" ]]; then
  find "$MSI_DIR" -maxdepth 1 -type f \( -name '*.msi' -o -name '*.UNSIGNED.txt' \) -print0 \
    | while IFS= read -r -d '' file; do
        cp -f "$file" "$OUT_DIR/"
      done
fi

if [[ -d "$PUBLISH_DIR" ]]; then
  mkdir -p "$OUT_DIR/publish-win-x64"
  # Portable layout is optional; copy only the main executable markers for size.
  if [[ -f "$PUBLISH_DIR/dotnet-glpi-agent.exe" ]]; then
    cp -f "$PUBLISH_DIR/dotnet-glpi-agent.exe" "$OUT_DIR/publish-win-x64/"
    cp -f "$PUBLISH_DIR/dotnet-glpi-agent.deps.json" "$OUT_DIR/publish-win-x64/" 2>/dev/null || true
  fi
fi

cp -f "$ROOT/THIRD-PARTY-NOTICES.md" "$OUT_DIR/THIRD-PARTY-NOTICES.md"
cp -f "$ROOT/LICENSE" "$OUT_DIR/LICENSE"

# Dependency SBOM snapshot (package list). Prefer CycloneDX when the tool is available.
SBOM="$OUT_DIR/sbom-dotnet-packages.txt"
{
  echo "# Dotnet GLPI Agent dependency snapshot"
  echo "# Generated: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "# Version: $VERSION"
  echo
  (cd "$ROOT" && DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/dotnet-cli}" \
    NUGET_PACKAGES="${NUGET_PACKAGES:-/tmp/nuget-packages}" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    dotnet list DotnetGlpiAgent.sln package --include-transitive 2>/dev/null || true)
} >"$SBOM"

if command -v cyclonedx-dotnet >/dev/null 2>&1; then
  (cd "$ROOT" && cyclonedx-dotnet DotnetGlpiAgent.sln -o "$OUT_DIR/sbom.cdx.json") || true
fi

(
  cd "$OUT_DIR"
  if command -v sha256sum >/dev/null 2>&1; then
    find . -type f ! -name 'SHA256SUMS' -print0 | sort -z | xargs -0 sha256sum >SHA256SUMS
  elif command -v shasum >/dev/null 2>&1; then
    find . -type f ! -name 'SHA256SUMS' -print0 | sort -z | xargs -0 shasum -a 256 >SHA256SUMS
  fi
)

if compgen -G "$OUT_DIR"/*.msi >/dev/null && ! compgen -G "$OUT_DIR"/*.UNSIGNED.txt >/dev/null; then
  echo "WARNING: MSI present without UNSIGNED marker — ensure production signatures were applied." >&2
fi

echo "Release artifacts written to $OUT_DIR"
