//go:build freebsd

package freebsd

import (
	"context"
	"runtime"
	"time"

	"github.com/shirou/gopsutil/v3/host"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// osCollector collects operating-system details (FreeBSD release, kernel, arch,
// boot time, host id) via gopsutil/host, which reads them from sysctl.
type osCollector struct{}

func init() { collector.Register(osCollector{}) }

func (osCollector) Name() string                      { return "freebsd/os" }
func (osCollector) Category() string                  { return "os" }
func (osCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "freebsd" }

// Collect fills the operating-system and hardware sections from host info.
func (osCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	info, err := host.InfoWithContext(ctx)
	if err != nil {
		return err
	}

	bootTime := time.Unix(int64(info.BootTime), 0).Format("2006-01-02 15:04:05")
	fullName := "FreeBSD"
	if info.PlatformVersion != "" {
		fullName += " " + info.PlatformVersion // e.g. "FreeBSD 14.1-RELEASE"
	}

	inv.SetOperatingSystem(func(o *inventory.OperatingSystem) {
		o.Name = "FreeBSD"
		o.Version = info.PlatformVersion
		o.FullName = fullName
		o.KernelName = "FreeBSD"
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
	return nil
}
