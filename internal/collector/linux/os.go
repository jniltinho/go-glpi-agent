//go:build linux

package linux

import (
	"context"
	"runtime"
	"time"

	"github.com/shirou/gopsutil/v3/host"
	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// osCollector collects operating system details (distro, version, kernel,
// hostname, boot time, host ID) via gopsutil/host and /etc/os-release.
type osCollector struct{}

// init registers the os collector with the collector registry.
func init() { collector.Register(osCollector{}) }

// Name returns the collector's registry name.
func (osCollector) Name() string { return "linux/os" }

// Category returns the inventory section this collector fills.
func (osCollector) Category() string { return "os" }

// IsEnabled reports whether the collector should run; it is Linux-only.
func (osCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

// Collect fills both the operating-system and hardware sections from host info,
// then overrides the OS full name with /etc/os-release PRETTY_NAME when present.
func (osCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	info, err := host.InfoWithContext(ctx)
	if err != nil {
		return err
	}

	bootTime := time.Unix(int64(info.BootTime), 0).Format("2006-01-02 15:04:05")
	fullName := info.Platform
	if info.PlatformVersion != "" {
		fullName += " " + info.PlatformVersion
	}

	inv.SetOperatingSystem(func(o *inventory.OperatingSystem) {
		o.Name = info.Platform
		o.Version = info.PlatformVersion
		o.FullName = fullName
		o.KernelName = "Linux"
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
		h.OSVersion = info.KernelVersion
		h.ArchName = info.KernelArch
		if h.UUID == "" {
			h.UUID = info.HostID
		}
	})

	// /etc/os-release fornece o nome "bonito" da distro como fallback.
	if pretty := osReleasePretty(); pretty != "" {
		inv.SetOperatingSystem(func(o *inventory.OperatingSystem) {
			o.FullName = pretty
		})
		inv.SetHardware(func(h *inventory.Hardware) { h.OSName = pretty })
	}
	return nil
}

// osReleasePretty returns the PRETTY_NAME value from /etc/os-release, or ""
// when the file is missing or the key is absent.
func osReleasePretty() string {
	content := sysutil.ReadFileTrim("/etc/os-release")
	if content == "" {
		return ""
	}
	for _, line := range sysutil.SplitLines(content) {
		if v, ok := cutQuoted(line, "PRETTY_NAME="); ok {
			return v
		}
	}
	return ""
}

// cutQuoted returns the value following prefix on line with surrounding single
// or double quotes stripped, and false if line does not start with prefix.
func cutQuoted(line, prefix string) (string, bool) {
	if len(line) < len(prefix) || line[:len(prefix)] != prefix {
		return "", false
	}
	v := line[len(prefix):]
	if len(v) >= 2 && (v[0] == '"' || v[0] == '\'') {
		v = v[1 : len(v)-1]
	}
	return v, true
}
