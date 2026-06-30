//go:build darwin

package macos

import (
	"context"
	"runtime"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// softwareCollector collects installed applications via
// `system_profiler SPApplicationsDataType`.
type softwareCollector struct{}

func init() { collector.Register(softwareCollector{}) }

func (softwareCollector) Name() string                      { return "macos/software" }
func (softwareCollector) Category() string                  { return "software" }
func (softwareCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "darwin" }

// Collect lists installed applications (name, version, publisher, install date).
// Enumerating applications is slow, so it runs within the engine's per-collector
// timeout and degrades to an empty section when unavailable.
func (softwareCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	for _, sw := range parseSPApplications(systemProfilerJSON(ctx, "SPApplicationsDataType")) {
		inv.AddSoftware(sw)
	}
	return nil
}
