//go:build windows

package windows

import (
	"context"
	"runtime"
	"strconv"
	"time"

	"github.com/shirou/gopsutil/v3/host"
	"golang.org/x/sys/windows/registry"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// osCollector collects operating-system details (product name, edition, version,
// build, kernel, boot time, arch) via gopsutil/host, enriched from the registry
// (HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion).
type osCollector struct{}

// init registers the os collector with the collector registry.
func init() { collector.Register(osCollector{}) }

// Name returns the collector's registry name.
func (osCollector) Name() string { return "windows/os" }

// Category returns the inventory section this collector fills.
func (osCollector) Category() string { return "os" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (osCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

// Collect fills the operating-system and hardware sections from host info, then
// overrides the product name/version with the richer registry values when present.
func (osCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	info, err := host.InfoWithContext(ctx)
	if err != nil {
		return err
	}

	bootTime := time.Unix(int64(info.BootTime), 0).Format("2006-01-02 15:04:05")

	reg := readOSRegistry()
	fullName := info.Platform // e.g. "Microsoft Windows 11 Pro"
	if reg.productName != "" {
		fullName = reg.productName
	}
	version := info.PlatformVersion
	if reg.fullBuild != "" {
		version = reg.fullBuild // e.g. "10.0.22631.4317"
	}

	inv.SetOperatingSystem(func(o *inventory.OperatingSystem) {
		o.Name = fullName
		o.Version = version
		o.FullName = fullName
		if reg.displayVersion != "" {
			o.FullName = fullName + " " + reg.displayVersion // "... 23H2"
		}
		o.KernelName = "Windows"
		o.KernelVersion = info.KernelVersion
		o.Arch = info.KernelArch
		o.BootTime = bootTime
		if o.FQDN == "" {
			o.FQDN = info.Hostname
		}
		o.HostID = info.HostID
	})

	inv.SetHardware(func(h *inventory.Hardware) {
		if h.Name == "" {
			h.Name = info.Hostname
		}
		h.OSName = fullName
		h.OSVersion = version
		h.ArchName = info.KernelArch
		if h.UUID == "" {
			h.UUID = info.HostID
		}
	})
	return nil
}

// osRegistry holds the values read from the CurrentVersion key.
type osRegistry struct {
	productName    string // "Windows 11 Pro"
	displayVersion string // "23H2"
	build          string // "22631"
	fullBuild      string // "10.0.22631.4317"
}

// readOSRegistry reads product name, display version and build/UBR from
// HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion. Missing values are left empty.
func readOSRegistry() osRegistry {
	var r osRegistry
	k, err := registry.OpenKey(registry.LOCAL_MACHINE,
		`SOFTWARE\Microsoft\Windows NT\CurrentVersion`, registry.QUERY_VALUE)
	if err != nil {
		return r
	}
	defer k.Close()

	r.productName, _, _ = k.GetStringValue("ProductName")
	r.displayVersion, _, _ = k.GetStringValue("DisplayVersion")
	r.build, _, _ = k.GetStringValue("CurrentBuild")
	ubr, _, _ := k.GetIntegerValue("UBR") // REG_DWORD

	// The Windows kernel reports as 10.0 for both Windows 10 and 11; compose
	// "10.0.<build>.<ubr>" when the build is available.
	if r.build != "" {
		r.fullBuild = "10.0." + r.build
		if ubr != 0 {
			r.fullBuild += "." + strconv.FormatUint(ubr, 10)
		}
	}
	return r
}
