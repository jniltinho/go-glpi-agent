//go:build darwin

package macos

import (
	"context"
	"runtime"

	"github.com/shirou/gopsutil/v3/mem"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// memoryCollector collects total/swap memory via gopsutil/mem and, where
// available, per-module details via `system_profiler SPMemoryDataType`. On Apple
// Silicon the RAM is soldered/unified and reported as a single module.
type memoryCollector struct{}

func init() { collector.Register(memoryCollector{}) }

func (memoryCollector) Name() string                      { return "macos/memory" }
func (memoryCollector) Category() string                  { return "memory" }
func (memoryCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "darwin" }

// Collect sets total memory and swap on the hardware section, then adds one
// memory entry per populated module from system_profiler (best-effort).
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

	for _, m := range parseSPMemory(systemProfilerJSON(ctx, "SPMemoryDataType")) {
		inv.AddMemory(m)
	}
	return nil
}
