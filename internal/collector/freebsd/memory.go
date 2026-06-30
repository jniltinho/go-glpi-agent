//go:build freebsd

package freebsd

import (
	"context"
	"runtime"

	"github.com/shirou/gopsutil/v3/mem"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// memoryCollector collects total and swap memory via gopsutil/mem (hw.physmem
// and the swap sysctls). FreeBSD exposes no per-DIMM detail via kenv, so physical
// slots are not reported (a dmidecode-based follow-up could add them).
type memoryCollector struct{}

func init() { collector.Register(memoryCollector{}) }

func (memoryCollector) Name() string                      { return "freebsd/memory" }
func (memoryCollector) Category() string                  { return "memory" }
func (memoryCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "freebsd" }

// Collect sets total memory and swap on the hardware section.
func (memoryCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	vm, err := mem.VirtualMemoryWithContext(ctx)
	if err != nil {
		return err
	}
	totalMB := int(vm.Total / 1024 / 1024)

	swapMB := 0
	if sw, err := mem.SwapMemoryWithContext(ctx); err == nil {
		swapMB = int(sw.Total / 1024 / 1024)
	}

	inv.SetHardware(func(h *inventory.Hardware) {
		h.Memory = totalMB
		h.Swap = swapMB
	})
	return nil
}
