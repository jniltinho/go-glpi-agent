// Package generic contains cross-cutting collectors that rely more on standard
// files (/etc) and common tools than on a specific OS.
package generic

import (
	"context"
	"os"
	"strings"

	"github.com/shirou/gopsutil/v3/host"
	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/sysutil"
)

type hostnameCollector struct{}

func init() { collector.Register(hostnameCollector{}) }

func (hostnameCollector) Name() string                      { return "generic/hostname" }
func (hostnameCollector) Category() string                  { return "hostname" }
func (hostnameCollector) IsEnabled(cfg *config.Config) bool { return true }

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
