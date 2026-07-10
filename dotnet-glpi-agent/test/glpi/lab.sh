#!/usr/bin/env bash
set -euo pipefail

HERE=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
COMPOSE=(docker compose --project-directory "$HERE" -f "$HERE/compose.yaml")
ARTIFACTS="$HERE/artifacts"

usage() {
  echo "usage: $0 {start|wait|enable|schema|inspect|stop|reset} {10|11|all}" >&2
  exit 2
}

versions() {
  case "${1:-}" in
    10) echo 10 ;;
    11) echo 11 ;;
    all) printf '10\n11\n' ;;
    *) usage ;;
  esac
}

profile() {
  echo "glpi$1"
}

port() {
  if [[ "$1" == 10 ]]; then echo "${GLPI10_PORT:-8180}"; else echo "${GLPI11_PORT:-8181}"; fi
}

wait_one() {
  local version=$1
  local deadline=$((SECONDS + 900))
  local url="http://127.0.0.1:$(port "$version")/"
  until curl --fail --silent --show-error --location --max-time 10 "$url" >/dev/null; do
    if (( SECONDS >= deadline )); then
      echo "GLPI $version did not become ready within 15 minutes" >&2
      "${COMPOSE[@]}" logs --tail=100 "glpi$version" "db$version" >&2
      return 1
    fi
    sleep 5
  done
}

console_dir() {
  local service=$1
  "${COMPOSE[@]}" exec -T "$service" sh -lc \
    'for d in /var/www/glpi /var/glpi; do test -f "$d/bin/console" && { printf "%s" "$d"; exit 0; }; done; exit 1'
}

enable_one() {
  local version=$1
  "${COMPOSE[@]}" exec -T "db$version" mariadb \
    -uglpi "-p${GLPI_DB_PASSWORD:-glpi-test-only}" glpi \
    -e "UPDATE glpi_configs SET value='1' WHERE context='inventory' AND name='enabled_inventory';"
  local directory
  directory=$(console_dir "glpi$version")
  "${COMPOSE[@]}" exec -T "glpi$version" sh -lc "cd '$directory' && php bin/console cache:clear --no-interaction"
}

schema_one() {
  local version=$1
  mkdir -p "$ARTIFACTS/glpi$version"
  local directory
  directory=$(console_dir "glpi$version")
  "${COMPOSE[@]}" cp \
    "glpi$version:$directory/vendor/glpi-project/inventory_format/inventory.schema.json" \
    "$ARTIFACTS/glpi$version/inventory.schema.json"
}

inspect_images() {
  mkdir -p "$ARTIFACTS"
  "${COMPOSE[@]}" images --format json | jq -s 'flatten | sort_by(.ContainerName)' >"$ARTIFACTS/image-digests.json"
}

command=${1:-}
target=${2:-}
[[ -n "$command" && -n "$target" ]] || usage

case "$command" in
  start)
    while read -r version; do
      "${COMPOSE[@]}" --profile "$(profile "$version")" up -d
    done < <(versions "$target")
    "$0" wait "$target"
    "$0" enable "$target"
    "$0" schema "$target"
    "$0" inspect "$target"
    ;;
  wait)
    while read -r version; do wait_one "$version"; done < <(versions "$target")
    ;;
  enable)
    while read -r version; do enable_one "$version"; done < <(versions "$target")
    ;;
  schema)
    while read -r version; do schema_one "$version"; done < <(versions "$target")
    ;;
  inspect)
    inspect_images
    "${COMPOSE[@]}" ps
    ;;
  stop)
    if [[ "$target" == all ]]; then
      "${COMPOSE[@]}" --profile glpi10 --profile glpi11 down
    else
      "${COMPOSE[@]}" stop "glpi$target" "db$target"
    fi
    ;;
  reset)
    if [[ "$target" == all ]]; then
      "${COMPOSE[@]}" --profile glpi10 --profile glpi11 down --volumes
    else
      "${COMPOSE[@]}" rm --stop --force "glpi$target" "db$target"
      docker volume rm -f "dotnet-glpi-agent-lab_glpi${target}_data" "dotnet-glpi-agent-lab_db${target}_data"
    fi
    ;;
  *) usage ;;
esac
