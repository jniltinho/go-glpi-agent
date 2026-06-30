package linux

import (
	"context"
	"runtime"
	"strconv"
	"strings"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// softwareCollector automatically detects the available package manager(s)
// and collects from all of them. Covers dpkg, rpm and pacman (design.md D8).
type softwareCollector struct{}

func init() { collector.Register(softwareCollector{}) }

func (softwareCollector) Name() string     { return "linux/software" }
func (softwareCollector) Category() string { return "software" }

func (softwareCollector) IsEnabled(cfg *config.Config) bool {
	if runtime.GOOS != "linux" {
		return false
	}
	return sysutil.CommandExists("dpkg-query") ||
		sysutil.CommandExists("rpm") ||
		sysutil.CommandExists("pacman")
}

func (softwareCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	if sysutil.CommandExists("dpkg-query") {
		collectDpkg(ctx, inv)
	}
	if sysutil.CommandExists("rpm") {
		collectRPM(ctx, inv)
	}
	if sysutil.CommandExists("pacman") {
		collectPacman(ctx, inv)
	}
	return nil
}

func collectDpkg(ctx context.Context, inv *inventory.Inventory) {
	const format = `${Package}\t${Version}\t${Architecture}\t${Installed-Size}\t${Section}\t${binary:Summary}\n`
	out, err := sysutil.RunContext(ctx, "dpkg-query", "-W", "-f", format)
	if err != nil {
		return
	}
	for _, line := range sysutil.SplitLines(out) {
		f := strings.Split(line, "\t")
		if len(f) < 2 || f[0] == "" {
			continue
		}
		sw := inventory.Software{
			Name:    f[0],
			Version: f[1],
			From:    "dpkg",
		}
		if len(f) > 2 {
			sw.Arch = f[2]
		}
		if len(f) > 3 {
			if kb, e := strconv.ParseInt(strings.TrimSpace(f[3]), 10, 64); e == nil {
				sw.FileSize = kb * 1024 // dpkg reports in KB
			}
		}
		if len(f) > 4 {
			sw.Section = f[4]
		}
		if len(f) > 5 {
			sw.Comments = f[5]
		}
		inv.AddSoftware(sw)
	}
}

func collectRPM(ctx context.Context, inv *inventory.Inventory) {
	const format = `%{NAME}\t%{VERSION}-%{RELEASE}\t%{ARCH}\t%{SIZE}\t%{INSTALLTIME:date}\t%{VENDOR}\t%{SUMMARY}\n`
	out, err := sysutil.RunContext(ctx, "rpm", "-qa", "--qf", format)
	if err != nil {
		return
	}
	for _, line := range sysutil.SplitLines(out) {
		f := strings.Split(line, "\t")
		if len(f) < 2 || f[0] == "" {
			continue
		}
		sw := inventory.Software{
			Name:    f[0],
			Version: f[1],
			From:    "rpm",
		}
		if len(f) > 2 {
			sw.Arch = f[2]
		}
		if len(f) > 3 {
			sw.FileSize, _ = strconv.ParseInt(strings.TrimSpace(f[3]), 10, 64)
		}
		if len(f) > 4 {
			sw.InstallDate = f[4]
		}
		if len(f) > 5 {
			sw.Publisher = f[5]
		}
		if len(f) > 6 {
			sw.Comments = f[6]
		}
		inv.AddSoftware(sw)
	}
}

func collectPacman(ctx context.Context, inv *inventory.Inventory) {
	out, err := sysutil.RunContext(ctx, "pacman", "-Q")
	if err != nil {
		return
	}
	for _, line := range sysutil.SplitLines(out) {
		f := strings.Fields(line)
		if len(f) < 2 {
			continue
		}
		inv.AddSoftware(inventory.Software{
			Name:    f[0],
			Version: f[1],
			From:    "pacman",
		})
	}
}
