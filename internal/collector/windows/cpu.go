//go:build windows

package windows

import (
	"context"
	"runtime"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// cpuCollector collects processor information (one entry per socket) via WMI
// Win32_Processor.
type cpuCollector struct{}

// init registers the cpu collector with the collector registry.
func init() { collector.Register(cpuCollector{}) }

// Name returns the collector's registry name.
func (cpuCollector) Name() string { return "windows/cpu" }

// Category returns the inventory section this collector fills.
func (cpuCollector) Category() string { return "cpu" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (cpuCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

// win32Processor mirrors the Win32_Processor properties we query. Each row is a
// physical socket.
type win32Processor struct {
	Name                      string
	Manufacturer              string
	MaxClockSpeed             uint32
	NumberOfCores             uint32
	NumberOfLogicalProcessors uint32
	Architecture              uint16
	ProcessorId               string
}

// Collect adds one CPU entry per socket reported by Win32_Processor.
func (cpuCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	var procs []win32Processor
	if err := queryWMI("SELECT Name, Manufacturer, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors, Architecture, ProcessorId FROM Win32_Processor", &procs); err != nil {
		return err
	}
	for _, p := range procs {
		inv.AddCPU(inventory.CPU{
			Name:         p.Name,
			Manufacturer: normalizeVendor(p.Manufacturer),
			Speed:        int(p.MaxClockSpeed),
			Core:         int(p.NumberOfCores),
			Thread:       int(p.NumberOfLogicalProcessors),
			CoreCount:    int(p.NumberOfLogicalProcessors),
			Arch:         cpuArch(p.Architecture),
			ID:           p.ProcessorId,
		})
	}
	return nil
}

// normalizeVendor translates the WMI manufacturer string to the name used by the
// Perl/GLPI agent.
func normalizeVendor(v string) string {
	switch v {
	case "GenuineIntel":
		return "Intel"
	case "AuthenticAMD":
		return "AMD"
	default:
		return v
	}
}

// cpuArch maps the Win32_Processor.Architecture code to a GLPI-canonical arch
// string (matches internal/transport/server's archGLPI targets).
func cpuArch(code uint16) string {
	switch code {
	case 0:
		return "i686" // x86
	case 5:
		return "arm"
	case 9:
		return "x86_64" // x64
	case 12:
		return "aarch64" // ARM64
	default:
		return runtime.GOARCH
	}
}
