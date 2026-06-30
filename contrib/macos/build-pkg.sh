#!/bin/bash
#
# Build a macOS .pkg and .dmg for go-glpi-agent. Runs on macOS (needs pkgbuild,
# productbuild and hdiutil). Unsigned by default; set APPSIGNID/INSTSIGNID to sign.
#
# Usage:
#   contrib/macos/build-pkg.sh <arch> <version> <binary-path> [out-dir]
#     <arch>        x86_64 | arm64
#     <version>     e.g. 1.0.0
#     <binary-path> path to the built darwin binary for that arch
#     [out-dir]     output directory (default: dist)
#
# Produces: <out-dir>/go-glpi-agent_<version>_<arch>.pkg
#           <out-dir>/go-glpi-agent_<version>_<arch>.dmg
set -euo pipefail

ARCH="${1:?arch required (x86_64|arm64)}"
VERSION="${2:?version required}"
BINARY="${3:?binary path required}"
OUTDIR="${4:-dist}"

IDENTIFIER="com.glpi.go-agent"
INSTALL_PREFIX="/usr/local/go-glpi-agent"
ROOT="$(cd "$(dirname "$0")" && pwd)"

case "$ARCH" in
    x86_64|arm64) ;;
    *) echo "Unknown arch: $ARCH (expected x86_64 or arm64)" >&2; exit 2 ;;
esac

[ -f "$BINARY" ] || { echo "Binary not found: $BINARY" >&2; exit 2; }

mkdir -p "$OUTDIR"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

PKG="$OUTDIR/go-glpi-agent_${VERSION}_${ARCH}.pkg"
DMG="$OUTDIR/go-glpi-agent_${VERSION}_${ARCH}.dmg"

echo "==> Staging payload"
PAYLOAD="$WORK/payload"
mkdir -p "$PAYLOAD$INSTALL_PREFIX/var"
mkdir -p "$PAYLOAD/Library/LaunchDaemons"
install -m 0755 "$BINARY" "$PAYLOAD$INSTALL_PREFIX/go-glpi-agent"
install -m 0644 "$ROOT/agent.cfg" "$PAYLOAD$INSTALL_PREFIX/agent.cfg"
install -m 0644 "$ROOT/com.glpi.go-agent.plist" "$PAYLOAD/Library/LaunchDaemons/com.glpi.go-agent.plist"

echo "==> Staging scripts"
SCRIPTS="$WORK/scripts"
mkdir -p "$SCRIPTS"
install -m 0755 "$ROOT/scripts/preinstall" "$SCRIPTS/preinstall"
install -m 0755 "$ROOT/scripts/postinstall" "$SCRIPTS/postinstall"

echo "==> pkgbuild (component package)"
COMPONENT="$WORK/component.pkg"
pkgbuild \
    --root "$PAYLOAD" \
    --scripts "$SCRIPTS" \
    --identifier "$IDENTIFIER" \
    --version "$VERSION" \
    --install-location "/" \
    ${INSTSIGNID:+--sign "$INSTSIGNID"} \
    "$COMPONENT"

echo "==> Distribution.xml"
DIST="$WORK/Distribution.xml"
cat >"$DIST" <<EOF
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<installer-gui-script minSpecVersion="2">
    <title>go-glpi-agent $VERSION ($ARCH)</title>
    <organization>com.glpi</organization>
    <options customize="never" require-scripts="false" hostArchitectures="$ARCH"/>
    <domains enable_anywhere="false" enable_currentUserHome="false" enable_localSystem="true"/>
    <choices-outline>
        <line choice="default">
            <line choice="$IDENTIFIER"/>
        </line>
    </choices-outline>
    <choice id="default"/>
    <choice id="$IDENTIFIER" visible="false">
        <pkg-ref id="$IDENTIFIER"/>
    </choice>
    <pkg-ref id="$IDENTIFIER" version="$VERSION" onConclusion="none">component.pkg</pkg-ref>
</installer-gui-script>
EOF

echo "==> productbuild (distribution installer) -> $PKG"
productbuild \
    --distribution "$DIST" \
    --package-path "$WORK" \
    ${INSTSIGNID:+--sign "$INSTSIGNID"} \
    "$PKG"

echo "==> hdiutil (DMG) -> $DMG"
rm -f "$DMG"
hdiutil create \
    -volname "go-glpi-agent $VERSION ($ARCH)" \
    -fs "HFS+" \
    -srcfolder "$PKG" \
    "$DMG"

echo "==> Done:"
ls -lh "$PKG" "$DMG"
