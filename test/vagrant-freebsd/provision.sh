#!/bin/sh
# Provisioner for the FreeBSD inventory test VM (POSIX sh).
# Args: $1 = GLPI server URL (front/inventory.php).
#
#   1. our Go agent: local XML + native JSON dump + send to GLPI
#   2. official FusionInventory agent (pkg): local XML + send to GLPI
#   3. per-section count comparison (Go vs FusionInventory)

set -u
GLPI="$1"
EXE=/usr/local/bin/go-glpi-agent
OUT=/tmp/out
mkdir -p "$OUT/go" "$OUT/ref"

install -m 0755 /tmp/go-glpi-agent "$EXE"

echo "=== go-glpi-agent version ==="
"$EXE" version
echo "GLPI server: $GLPI"

echo
echo "=== [1] go-glpi-agent: local XML + native JSON dump ==="
GFI_DUMP_JSON="$OUT/go/inventory.json" "$EXE" run --local "$OUT/go" --debug
echo "--- sending to GLPI ---"
"$EXE" run --server "$GLPI" --force --debug 2>&1 | grep -Ei 'native|sent|status|error' | head -n 6

echo
echo "=== [2] official FusionInventory agent (reference, p5-FusionInventory-Agent) ==="
export ASSUME_ALWAYS_YES=yes
REF=""
for cand in p5-FusionInventory-Agent fusioninventory-agent glpi-agent; do
  if pkg install -y "$cand" >>/tmp/pkg.log 2>&1; then
    REF=$(command -v fusioninventory-agent || command -v glpi-agent || echo /usr/local/bin/fusioninventory-agent)
    echo "installed reference: $cand -> $REF"
    break
  fi
done
if [ -n "$REF" ] && [ -x "$REF" ]; then
  echo "--- official agent: local XML ---"
  "$REF" --config=none --local "$OUT/ref" --no-category=printer 2>&1 | tail -n 4
  echo "--- official agent: sending to GLPI ---"
  "$REF" --config=none --server "$GLPI" --force 2>&1 | grep -Ei 'success|error|sending' | head -n 6
else
  echo "no reference agent installable (see /tmp/pkg.log) - skipping reference"
fi

echo
echo "=== [3] per-section comparison (Go vs FusionInventory) ==="
count() { # $1=dir $2=section
  f=$(ls "$1"/*.xml "$1"/*.ocs 2>/dev/null | head -n 1)
  if [ -n "$f" ]; then
    grep -c "<$2>" "$f" 2>/dev/null || true
  else
    echo 0
  fi
}
printf '%-16s %6s %9s\n' SECTION GO OFFICIAL
for s in HARDWARE BIOS OPERATINGSYSTEM CPUS MEMORIES DRIVES STORAGES NETWORKS SOFTWARES USBDEVICES LOCAL_USERS LOCAL_GROUPS; do
  printf '%-16s %6s %9s\n' "$s" "$(count "$OUT/go" "$s")" "$(count "$OUT/ref" "$s")"
done

echo
echo "Provisioning complete. Check GLPI assets for this FreeBSD host."
