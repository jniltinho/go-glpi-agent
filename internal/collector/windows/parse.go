// This file holds the pure, OS-independent parsing helpers used by the Windows
// collectors. It deliberately carries NO build tag and imports nothing
// Windows-only, so the logic is unit-testable on any platform (the WMI/registry
// I/O lives in the build-tagged collector files).

package windows

import (
	"strings"

	"go-glpi-agent/internal/inventory"
)

// uninstallEntry is the raw set of values read from one uninstall registry
// subkey. It is a plain struct so toSoftware can be unit-tested without a live
// registry.
type uninstallEntry struct {
	DisplayName     string
	DisplayVersion  string
	Publisher       string
	InstallDate     string // "YYYYMMDD"
	EstimatedSizeKB uint64
	SystemComponent uint64 // 1 = hidden from Add/Remove Programs
}

// toSoftware maps an uninstall entry to an inventory.Software, returning ok=false
// for entries that must be skipped (no display name, or a system component).
func (e uninstallEntry) toSoftware(from string) (inventory.Software, bool) {
	if e.DisplayName == "" || e.SystemComponent == 1 {
		return inventory.Software{}, false
	}
	return inventory.Software{
		Name:        e.DisplayName,
		Version:     e.DisplayVersion,
		Publisher:   e.Publisher,
		From:        from,
		InstallDate: dashDate(e.InstallDate),
		FileSize:    int64(e.EstimatedSizeKB) * 1024,
	}, true
}

// dashDate converts a registry "YYYYMMDD" install date to "YYYY-MM-DD", leaving
// other formats untouched so the JSON serializer can normalize or drop them.
func dashDate(s string) string {
	if len(s) == 8 && isDigits(s) {
		return s[0:4] + "-" + s[4:6] + "-" + s[6:8]
	}
	return s
}

// isDigits reports whether s is non-empty and all ASCII digits.
func isDigits(s string) bool {
	for i := 0; i < len(s); i++ {
		if s[i] < '0' || s[i] > '9' {
			return false
		}
	}
	return s != ""
}

// cimDate converts a WMI CIM_DATETIME ("yyyymmddHHMMSS.ffffff±UUU") to
// YYYY-MM-DD, returning "" when it is too short to parse. The native JSON
// serializer further normalizes/validates this value.
func cimDate(s string) string {
	if len(s) < 8 {
		return ""
	}
	return s[0:4] + "-" + s[4:6] + "-" + s[6:8]
}

// parseUSBID extracts the 4-hex vendor and product ids (and, when present, the
// instance serial) from a PnP DeviceID. The format is backslash-separated:
// "USB\VID_046D&PID_C52B\<instance>". The VID/PID live in the middle segment
// (joined by '&'); the instance segment is a real serial only when it carries no
// '&' (bus-enumerated paths like "5&1a2b&0&2" are not serials).
func parseUSBID(deviceID string) (vid, pid, serial string) {
	segs := strings.Split(deviceID, `\`)
	if len(segs) < 2 {
		return "", "", ""
	}
	for _, part := range strings.Split(segs[1], "&") {
		switch {
		case strings.HasPrefix(part, "VID_"):
			vid = strings.ToLower(strings.TrimPrefix(part, "VID_"))
		case strings.HasPrefix(part, "PID_"):
			pid = strings.ToLower(strings.TrimPrefix(part, "PID_"))
		}
	}
	if len(segs) >= 3 && !strings.Contains(segs[2], "&") {
		serial = segs[2]
	}
	return vid, pid, serial
}
