package linux

import (
	"context"
	"runtime"
	"time"

	"github.com/shirou/gopsutil/v3/host"
	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/sysutil"
)

type osCollector struct{}

func init() { collector.Register(osCollector{}) }

func (osCollector) Name() string                      { return "linux/os" }
func (osCollector) Category() string                  { return "os" }
func (osCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

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
