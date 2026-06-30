//go:build linux

package linux

import (
	"context"
	"regexp"
	"runtime"
	"strconv"
	"strings"

	"github.com/shirou/gopsutil/v3/mem"
	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// memoryCollector collects total/swap memory via gopsutil/mem and, when
// dmidecode is available (root), per-slot physical memory details.
type memoryCollector struct{}

// init registers the memory collector with the collector registry.
func init() { collector.Register(memoryCollector{}) }

// Name returns the collector's registry name.
func (memoryCollector) Name() string { return "linux/memory" }

// Category returns the inventory section this collector fills.
func (memoryCollector) Category() string { return "memory" }

// IsEnabled reports whether the collector should run; it is Linux-only.
func (memoryCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

// Collect sets total memory and swap on the hardware section, then adds one
// memory entry per populated DIMM slot when dmidecode is present. The slot
// scan fails gracefully (no root, no dmidecode) without aborting collection.
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

	// Physical slots via dmidecode (requires root). Fails gracefully.
	if sysutil.CommandExists("dmidecode") {
		collectMemorySlots(ctx, inv)
	}
	return nil
}

var dmiMemBlock = regexp.MustCompile(`(?m)^Memory Device\b`)

// collectMemorySlots runs "dmidecode -t 17", splits its output into per-slot
// "Memory Device" blocks, and adds an inventory entry for each populated slot.
// Empty slots ("No Module Installed") are skipped; dmidecode failures are ignored.
func collectMemorySlots(ctx context.Context, inv *inventory.Inventory) {
	out, err := sysutil.RunContext(ctx, "dmidecode", "-t", "17")
	if err != nil {
		return
	}
	// split into "Memory Device" blocks
	idxs := dmiMemBlock.FindAllStringIndex(out, -1)
	for i, loc := range idxs {
		end := len(out)
		if i+1 < len(idxs) {
			end = idxs[i+1][0]
		}
		block := out[loc[0]:end]
		m := parseMemoryBlock(block)
		// skip empty slots ("No Module Installed")
		if m.Capacity == 0 && m.SerialNumber == "" {
			continue
		}
		m.NumSlots = i + 1
		inv.AddMemory(m)
	}
}

// parseMemoryBlock parses a single dmidecode "Memory Device" block into a
// Memory entry, dropping placeholder values like "Unknown" / "Not Specified".
func parseMemoryBlock(block string) inventory.Memory {
	var m inventory.Memory
	for _, line := range sysutil.SplitLines(block) {
		line = strings.TrimSpace(line)
		key, val, ok := strings.Cut(line, ":")
		if !ok {
			continue
		}
		key = strings.TrimSpace(key)
		val = strings.TrimSpace(val)
		switch key {
		case "Size":
			m.Capacity = parseDmiSizeMB(val)
		case "Type":
			if val != "Unknown" {
				m.Type = val
			}
		case "Speed":
			if val != "Unknown" {
				m.Speed = val
			}
		case "Manufacturer":
			if val != "Unknown" && val != "Not Specified" {
				m.Manufacturer = val
			}
		case "Serial Number":
			if val != "Unknown" && val != "Not Specified" {
				m.SerialNumber = val
			}
		case "Form Factor":
			m.Caption = val
		case "Locator":
			m.Description = val
		}
	}
	return m
}

// parseDmiSizeMB converts "16384 MB" / "16 GB" to MB.
func parseDmiSizeMB(s string) int {
	fields := strings.Fields(s)
	if len(fields) < 1 {
		return 0
	}
	n, err := strconv.Atoi(fields[0])
	if err != nil {
		return 0
	}
	if len(fields) >= 2 && strings.EqualFold(fields[1], "GB") {
		return n * 1024
	}
	return n
}
