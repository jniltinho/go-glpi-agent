#!/usr/bin/env bash
# Orchestrate the Dotnet GLPI Agent Windows acceptance lab.
set -euo pipefail

HERE=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd -- "$HERE/../.." && pwd)
GLPI_LAB="$ROOT/test/glpi"
ARTIFACTS="$HERE/artifacts"
STAGES=${STAGES:-install,local,schema,submit,lifecycle,collect}
GLPI_VERSION=${GLPI_VERSION:-10}
GLPI_SERVER=${GLPI_SERVER:-}
KEEP_RESOURCES=${KEEP_RESOURCES:-0}
PROVIDER=${PROVIDER:-virtualbox}

usage() {
  cat <<EOF
usage: $0 {prepare|start-glpi|up|provision|collect|destroy|status} [options]

Environment:
  GLPI_VERSION     10|11 (default 10)
  GLPI_SERVER      inventory URL (default VirtualBox NAT to host port)
  STAGES           comma stages for provision.ps1
  KEEP_RESOURCES   1 to leave VM contents after provision
  PROVIDER         virtualbox|hyperv
  VERSION          package version used when locating MSI (default 0.1.0)

Stages: install,local,schema,submit,compare,lifecycle,collect,all
EOF
  exit 2
}

cmd=${1:-}
[[ -n "$cmd" ]] || usage
shift || true

VERSION=${VERSION:-0.1.0}
mkdir -p "$ARTIFACTS" "$ARTIFACTS/ref" "$ARTIFACTS/previous" "$ARTIFACTS/tools"

default_server() {
  if [[ "$GLPI_VERSION" == "11" ]]; then
    echo "http://10.0.2.2:8181/front/inventory.php"
  else
    echo "http://10.0.2.2:8180/front/inventory.php"
  fi
}

[[ -n "$GLPI_SERVER" ]] || GLPI_SERVER=$(default_server)

prepare() {
  echo "Looking for MSI under $ROOT/artifacts/msi ..."
  local msi
  msi=$(ls -1 "$ROOT/artifacts/msi"/dotnet-glpi-agent-*-win-x64.msi 2>/dev/null | sort | tail -1 || true)
  if [[ -z "$msi" ]]; then
    echo "No MSI found. Build on Windows: make package-windows VERSION=$VERSION" >&2
    echo "Then copy artifacts/msi/*.msi into $ARTIFACTS/" >&2
    exit 1
  fi
  cp -f "$msi" "$ARTIFACTS/"
  if [[ -d "$ROOT/artifacts/tools/glpi-schema-validator" ]]; then
    mkdir -p "$ARTIFACTS/tools/glpi-schema-validator"
    cp -a "$ROOT/artifacts/tools/glpi-schema-validator/." "$ARTIFACTS/tools/glpi-schema-validator/" || true
  fi
  # Optional Go reference from parent repo
  if [[ -f "$ROOT/../dist/go-glpi-agent.exe" ]]; then
    cp -f "$ROOT/../dist/go-glpi-agent.exe" "$ARTIFACTS/ref/"
  fi
  echo "Prepared:"
  find "$ARTIFACTS" -type f | sort
}

start_glpi() {
  "$GLPI_LAB/lab.sh" start "$GLPI_VERSION"
}

up() {
  prepare
  export GLPI_SERVER GLPI_VERSION STAGES KEEP_RESOURCES
  (cd "$HERE" && vagrant up --provider "$PROVIDER")
}

provision() {
  export GLPI_SERVER GLPI_VERSION STAGES KEEP_RESOURCES
  (cd "$HERE" && vagrant provision)
}

collect() {
  mkdir -p "$HERE/results"
  (cd "$HERE" && vagrant winrm -c "if (Test-Path C:\\dotnet-glpi-agent-test\\out) { Compress-Archive -Force -Path C:\\dotnet-glpi-agent-test\\out\\* -DestinationPath C:\\dotnet-glpi-agent-test\\out-bundle.zip }")
  (cd "$HERE" && vagrant winrm -c "Get-Content C:\\dotnet-glpi-agent-test\\out\\summary.json" ) \
    | tee "$HERE/results/summary.json" || true
  echo "Guest outputs remain under C:\\dotnet-glpi-agent-test\\out (use synced folder or winrm to copy)."
}

destroy() {
  (cd "$HERE" && vagrant destroy -f)
  if [[ "$KEEP_RESOURCES" != "1" ]]; then
    echo "VM destroyed. Host artifacts under $ARTIFACTS preserved."
  fi
}

status() {
  (cd "$HERE" && vagrant status) || true
  echo "GLPI_SERVER=$GLPI_SERVER"
  echo "STAGES=$STAGES"
}

case "$cmd" in
  prepare) prepare ;;
  start-glpi) start_glpi ;;
  up) up ;;
  provision) provision ;;
  collect) collect ;;
  destroy) destroy ;;
  status) status ;;
  *) usage ;;
esac
