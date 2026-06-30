//go:build darwin

package macos

import (
	"context"
	"runtime"
	"strconv"

	"github.com/shirou/gopsutil/v3/cpu"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// cpuCollector collects processor information. Counts come from gopsutil/cpu; the
// model/speed come from sysctl (machdep.cpu.brand_string on Intel) with a
// system_profiler "Chip"/"Processor Name" fallback for Apple Silicon, where
// machdep.cpu.brand_string does not exist.
type cpuCollector struct{}

func init() { collector.Register(cpuCollector{}) }

func (cpuCollector) Name() string                      { return "macos/cpu" }
func (cpuCollector) Category() string                  { return "cpu" }
func (cpuCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "darwin" }

// Collect adds one CPU entry for the package: name from sysctl/system_profiler,
// physical cores from hw.physicalcpu, threads from hw.logicalcpu, speed from
// hw.cpufrequency (Intel; absent on Apple Silicon).
func (cpuCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	physical, _ := cpu.CountsWithContext(ctx, false)
	logical, _ := cpu.CountsWithContext(ctx, true)
	if physical <= 0 {
		if n := sysctlInt(ctx, "hw.physicalcpu"); n > 0 {
			physical = n
		} else {
			physical = logical
		}
	}
	if logical <= 0 {
		logical = sysctlInt(ctx, "hw.logicalcpu")
	}

	name := sysctlStr(ctx, "machdep.cpu.brand_string")
	if name == "" {
		// Apple Silicon: no brand_string; use system_profiler's chip name.
		name = appleChipName(ctx)
	}
	if name == "" {
		if infos, err := cpu.InfoWithContext(ctx); err == nil && len(infos) > 0 {
			name = infos[0].ModelName
		}
	}

	// hw.cpufrequency is in Hz on Intel; Apple Silicon does not expose it.
	speedMHz := 0
	if hz := sysctlInt64(ctx, "hw.cpufrequency"); hz > 0 {
		speedMHz = int(hz / 1000 / 1000)
	} else if infos, err := cpu.InfoWithContext(ctx); err == nil && len(infos) > 0 {
		speedMHz = int(infos[0].Mhz)
	}

	manufacturer := "Apple"
	if runtime.GOARCH == "amd64" {
		manufacturer = "Intel"
	}

	inv.AddCPU(inventory.CPU{
		Name:         name,
		Manufacturer: manufacturer,
		Speed:        speedMHz,
		Core:         physical,
		Thread:       logical,
		CoreCount:    logical,
		Arch:         runtime.GOARCH,
		FamilyNumber: sysctlStr(ctx, "machdep.cpu.family"),
		Model:        sysctlStr(ctx, "machdep.cpu.model"),
		Stepping:     sysctlStr(ctx, "machdep.cpu.stepping"),
	})
	return nil
}

// appleChipName reads the "Chip" (Apple Silicon) or "Processor Name" (Intel)
// field from system_profiler SPHardwareDataType.
func appleChipName(ctx context.Context) string {
	hw := systemProfilerHardware(ctx)
	if hw.ChipType != "" {
		return hw.ChipType
	}
	return hw.CPUType
}

// sysctlStr returns the string value of a sysctl key, or "".
func sysctlStr(ctx context.Context, key string) string {
	out, err := sysutil.RunContext(ctx, "sysctl", "-n", key)
	if err != nil {
		return ""
	}
	return firstLine(out)
}

// sysctlInt returns the int value of a sysctl key, or 0.
func sysctlInt(ctx context.Context, key string) int {
	n, _ := strconv.Atoi(sysctlStr(ctx, key))
	return n
}

// sysctlInt64 returns the int64 value of a sysctl key, or 0.
func sysctlInt64(ctx context.Context, key string) int64 {
	n, _ := strconv.ParseInt(sysctlStr(ctx, key), 10, 64)
	return n
}
