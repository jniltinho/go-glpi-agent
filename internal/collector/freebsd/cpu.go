//go:build freebsd

package freebsd

import (
	"context"
	"runtime"

	"github.com/shirou/gopsutil/v3/cpu"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// cpuCollector collects processor information via gopsutil/cpu, which reads
// hw.model and the CPU sysctls on FreeBSD.
type cpuCollector struct{}

func init() { collector.Register(cpuCollector{}) }

func (cpuCollector) Name() string                      { return "freebsd/cpu" }
func (cpuCollector) Category() string                  { return "cpu" }
func (cpuCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "freebsd" }

// Collect adds one CPU entry. FreeBSD's gopsutil typically reports a single
// InfoStat for the package; physical/logical counts come from CountsWithContext.
func (cpuCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	infos, err := cpu.InfoWithContext(ctx)
	if err != nil {
		return err
	}
	physical, _ := cpu.CountsWithContext(ctx, false)
	logical, _ := cpu.CountsWithContext(ctx, true)
	if physical <= 0 {
		physical = logical
	}

	name, mhz := "", 0
	if len(infos) > 0 {
		name = infos[0].ModelName
		mhz = int(infos[0].Mhz)
	}
	inv.AddCPU(inventory.CPU{
		Name:      name,
		Speed:     mhz,
		Core:      physical,
		Thread:    logical,
		CoreCount: logical,
		Arch:      runtime.GOARCH,
	})
	return nil
}
