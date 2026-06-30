//go:build windows

package windows

import (
	"context"
	"runtime"

	"golang.org/x/sys/windows/registry"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// softwareCollector enumerates installed software from the Windows uninstall
// registry keys (64-bit, 32-bit/WOW6432Node, and per-user). It deliberately
// avoids WMI Win32_Product, which is slow and triggers MSI self-repair.
type softwareCollector struct{}

// init registers the software collector with the collector registry.
func init() { collector.Register(softwareCollector{}) }

// Name returns the collector's registry name.
func (softwareCollector) Name() string { return "windows/software" }

// Category returns the inventory section this collector fills.
func (softwareCollector) Category() string { return "software" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (softwareCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

const uninstallPath = `SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
const uninstallPathWOW = `SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`

// uninstallSource identifies one registry hive/view to scan.
type uninstallSource struct {
	root registry.Key
	path string
	from string // value stored in Software.From
}

// Collect walks every uninstall key, mapping each entry to a software record,
// de-duplicated by name+version.
func (softwareCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	sources := []uninstallSource{
		{registry.LOCAL_MACHINE, uninstallPath, "registry"},
		{registry.LOCAL_MACHINE, uninstallPathWOW, "registry-wow64"},
		// ponytail: HKCU here is the *running account's* hive. Under the Scheduled
		// Task (SYSTEM) this only sees machine-wide per-user installs, not other
		// users' software. Loading each profile's NTUSER.DAT to enumerate real
		// per-user installs is the upgrade path if that coverage is needed.
		{registry.CURRENT_USER, uninstallPath, "registry-user"},
	}
	seen := map[string]bool{}
	for _, src := range sources {
		for _, e := range readUninstallEntries(src.root, src.path) {
			sw, ok := e.toSoftware(src.from)
			if !ok {
				continue
			}
			key := sw.Name + "\x00" + sw.Version
			if seen[key] {
				continue
			}
			seen[key] = true
			inv.AddSoftware(sw)
		}
	}
	return nil
}

// readUninstallEntries opens the uninstall key under root/path and reads each
// subkey into an uninstallEntry. A missing key yields no entries.
func readUninstallEntries(root registry.Key, path string) []uninstallEntry {
	k, err := registry.OpenKey(root, path, registry.ENUMERATE_SUB_KEYS|registry.QUERY_VALUE)
	if err != nil {
		return nil
	}
	defer k.Close()

	names, err := k.ReadSubKeyNames(-1)
	if err != nil {
		return nil
	}
	out := make([]uninstallEntry, 0, len(names))
	for _, name := range names {
		sub, err := registry.OpenKey(root, path+`\`+name, registry.QUERY_VALUE)
		if err != nil {
			continue
		}
		var e uninstallEntry
		e.DisplayName, _, _ = sub.GetStringValue("DisplayName")
		e.DisplayVersion, _, _ = sub.GetStringValue("DisplayVersion")
		e.Publisher, _, _ = sub.GetStringValue("Publisher")
		e.InstallDate, _, _ = sub.GetStringValue("InstallDate")
		e.EstimatedSizeKB, _, _ = sub.GetIntegerValue("EstimatedSize")
		e.SystemComponent, _, _ = sub.GetIntegerValue("SystemComponent")
		sub.Close()
		out = append(out, e)
	}
	return out
}
