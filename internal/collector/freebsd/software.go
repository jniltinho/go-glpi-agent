//go:build freebsd

package freebsd

import (
	"context"
	"runtime"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// softwareCollector collects installed packages via `pkg query`.
type softwareCollector struct{}

func init() { collector.Register(softwareCollector{}) }

func (softwareCollector) Name() string     { return "freebsd/software" }
func (softwareCollector) Category() string { return "software" }
func (softwareCollector) IsEnabled(cfg *config.Config) bool {
	return runtime.GOOS == "freebsd" && sysutil.CommandExists("pkg")
}

// Collect lists installed packages (name, version, ABI/arch, size, comment). A
// system where pkg is present but not bootstrapped returns no rows, which is fine.
func (softwareCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	out, err := sysutil.RunContext(ctx, "pkg", "query", "%n\t%v\t%q\t%sb\t%c")
	if err != nil {
		return nil // pkg present but no local database / not bootstrapped: best-effort
	}
	for _, sw := range parsePkgQuery(out) {
		inv.AddSoftware(sw)
	}
	return nil
}
