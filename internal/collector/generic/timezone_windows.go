//go:build windows

package generic

import "golang.org/x/sys/windows/registry"

// osTimezoneName returns the Windows timezone name (e.g. "E. South America
// Standard Time") from the registry, or "" when it cannot be read. GLPI accepts
// the Windows zone name as the timezone identifier.
func osTimezoneName() string {
	k, err := registry.OpenKey(registry.LOCAL_MACHINE,
		`SYSTEM\CurrentControlSet\Control\TimeZoneInformation`, registry.QUERY_VALUE)
	if err != nil {
		return ""
	}
	defer k.Close()
	name, _, _ := k.GetStringValue("TimeZoneKeyName")
	return name
}
