// Package generic contains cross-cutting collectors that rely more on standard
// files (/etc) and common tools than on a specific OS.
package generic

import (
	"context"
	"os"
	"strings"

	"github.com/shirou/gopsutil/v3/host"
	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// hostnameCollector resolves the machine's short hostname, DNS domain, and FQDN
// and records them in the hardware and operating-system sections.
type hostnameCollector struct{}

// init registers the hostname collector with the collector registry.
func init() { collector.Register(hostnameCollector{}) }

// Name returns the collector identifier.
func (hostnameCollector) Name() string { return "generic/hostname" }

// Category returns the inventory category controlled by --no-category.
func (hostnameCollector) Category() string { return "hostname" }

// IsEnabled always returns true; hostname is collected on every host.
func (hostnameCollector) IsEnabled(cfg *config.Config) bool { return true }

// Collect splits the OS hostname into short name and domain, falling back to
// resolv.conf for the domain and to gopsutil for the FQDN, then stores them
// without overwriting a hardware Name already set by another collector.
func (hostnameCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	hostname, _ := os.Hostname()

	// short hostname + domain
	short := hostname
	domain := ""
	if i := strings.Index(hostname, "."); i >= 0 {
		short = hostname[:i]
		domain = hostname[i+1:]
	}
	if domain == "" {
		domain = resolvDomain()
	}

	fqdn := hostname
	if info, err := host.InfoWithContext(ctx); err == nil && info.Hostname != "" {
		fqdn = info.Hostname
	}

	inv.SetHardware(func(h *inventory.Hardware) {
		if h.Name == "" {
			h.Name = short
		}
		if domain != "" {
			h.DNS = domain
		}
	})
	inv.SetOperatingSystem(func(o *inventory.OperatingSystem) {
		o.FQDN = fqdn
		if domain != "" {
			o.DNSDomain = domain
		}
	})
	return nil
}

// resolvDomain reads the first "domain" or "search" directive from /etc/resolv.conf.
func resolvDomain() string {
	content := sysutil.ReadFileTrim("/etc/resolv.conf")
	for _, line := range sysutil.SplitLines(content) {
		line = strings.TrimSpace(line)
		if d, ok := strings.CutPrefix(line, "domain "); ok {
			return strings.TrimSpace(d)
		}
	}
	for _, line := range sysutil.SplitLines(content) {
		line = strings.TrimSpace(line)
		if d, ok := strings.CutPrefix(line, "search "); ok {
			fields := strings.Fields(d)
			if len(fields) > 0 {
				return fields[0]
			}
		}
	}
	return ""
}
