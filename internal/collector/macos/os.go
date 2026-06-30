//go:build darwin

package macos

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

// osCollector collects operating-system details (macOS product name/version,
// build, Darwin kernel, arch, boot time, host id) via gopsutil/host and sw_vers.
type osCollector struct{}

func init() { collector.Register(osCollector{}) }

func (osCollector) Name() string                      { return "macos/os" }
func (osCollector) Category() string                  { return "os" }
func (osCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "darwin" }

// Collect fills the operating-system and hardware sections. The marketing name
// and version come from sw_vers (e.g. "macOS" / "14.5" / build "23F79"); gopsutil
// supplies the Darwin kernel version, arch, boot time and hostname.
func (osCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	info, err := host.InfoWithContext(ctx)
	if err != nil {
		return err
	}

	name := swVers(ctx, "-productName")       // "macOS" (or "Mac OS X" on old releases)
	version := swVers(ctx, "-productVersion") // "14.5"
	build := swVers(ctx, "-buildVersion")     // "23F79"
	if name == "" {
		name = "macOS"
	}
	if version == "" {
		version = info.PlatformVersion
	}

	fullName := name
	if version != "" {
		fullName += " " + version
	}
	if build != "" {
		fullName += " (" + build + ")"
	}

	bootTime := time.Unix(int64(info.BootTime), 0).Format("2006-01-02 15:04:05")

	inv.SetOperatingSystem(func(o *inventory.OperatingSystem) {
		o.Name = name
		o.Version = version
		o.FullName = fullName
		o.KernelName = "Darwin"
		o.KernelVersion = info.KernelVersion
		o.Arch = info.KernelArch // "x86_64" or "arm64"
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

// swVers returns a single field from sw_vers (e.g. -productVersion), or "" when
// the tool is unavailable or the field is empty.
func swVers(ctx context.Context, flag string) string {
	out, err := sysutil.RunContext(ctx, "sw_vers", flag)
	if err != nil {
		return ""
	}
	return firstLine(out)
}
