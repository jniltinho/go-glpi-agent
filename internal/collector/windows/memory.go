//go:build windows

package windows

import (
	"context"
	"runtime"
	"strconv"

	"github.com/shirou/gopsutil/v3/mem"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// memoryCollector collects total/swap memory via gopsutil/mem and per-slot
// physical memory details via WMI Win32_PhysicalMemory.
type memoryCollector struct{}

// init registers the memory collector with the collector registry.
func init() { collector.Register(memoryCollector{}) }

// Name returns the collector's registry name.
func (memoryCollector) Name() string { return "windows/memory" }

// Category returns the inventory section this collector fills.
func (memoryCollector) Category() string { return "memory" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (memoryCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

// win32PhysicalMemory mirrors the Win32_PhysicalMemory properties we query.
type win32PhysicalMemory struct {
	Capacity      uint64
	Speed         uint32
	MemoryType    uint16
	Manufacturer  string
	SerialNumber  string
	DeviceLocator string
	FormFactor    uint16
}

// Collect sets total memory and swap on the hardware section, then adds one
// memory entry per populated DIMM slot from Win32_PhysicalMemory.
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

	var slots []win32PhysicalMemory
	if err := queryWMI("SELECT Capacity, Speed, MemoryType, Manufacturer, SerialNumber, DeviceLocator, FormFactor FROM Win32_PhysicalMemory", &slots); err != nil {
		return nil // total memory already recorded; slot detail is best-effort
	}
	for i, s := range slots {
		if s.Capacity == 0 {
			continue // empty slot
		}
		speed := ""
		if s.Speed != 0 {
			speed = strconv.FormatUint(uint64(s.Speed), 10)
		}
		inv.AddMemory(inventory.Memory{
			Capacity:     int(s.Capacity / 1024 / 1024),
			Type:         memoryType(s.MemoryType),
			Speed:        speed,
			Manufacturer: sysutil.CleanDMI(s.Manufacturer),
			SerialNumber: sysutil.CleanDMI(s.SerialNumber),
			Description:  s.DeviceLocator,
			Caption:      formFactor(s.FormFactor),
			NumSlots:     i + 1,
		})
	}
	return nil
}

// memoryType maps the SMBIOS Win32_PhysicalMemory.MemoryType code to a name.
// Modern firmware often reports 0 (Unknown) here and exposes the real type via
// SMBIOSMemoryType instead; an empty string is fine for GLPI.
func memoryType(code uint16) string {
	switch code {
	case 20:
		return "DDR"
	case 21:
		return "DDR2"
	case 24:
		return "DDR3"
	case 26:
		return "DDR4"
	case 34:
		return "DDR5"
	default:
		return ""
	}
}

// formFactor maps the Win32_PhysicalMemory.FormFactor code to a caption.
func formFactor(code uint16) string {
	switch code {
	case 8:
		return "DIMM"
	case 12:
		return "SODIMM"
	default:
		return ""
	}
}
