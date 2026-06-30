#!/bin/bash
# Uninstall go-glpi-agent from macOS: unload the LaunchDaemon, remove the files
# and forget the package receipt. Run with sudo.
set -e

if [ "$(id -u)" -ne 0 ]; then
    echo "Please run as root (sudo $0)" >&2
    exit 1
fi

PLIST="/Library/LaunchDaemons/com.glpi.go-agent.plist"

echo "Unloading LaunchDaemon..."
if [ -f "$PLIST" ]; then
    launchctl bootout system "$PLIST" 2>/dev/null || launchctl unload "$PLIST" 2>/dev/null || true
    rm -f "$PLIST"
fi

echo "Removing files..."
rm -rf /usr/local/go-glpi-agent
rm -f /var/log/go-glpi-agent.log

echo "Forgetting package receipt..."
pkgutil --forget com.glpi.go-agent 2>/dev/null || true

echo "go-glpi-agent uninstalled."
