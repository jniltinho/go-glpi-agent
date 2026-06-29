package linux

import (
	"context"
	"regexp"
	"runtime"
	"strconv"
	"strings"

	"github.com/shirou/gopsutil/v3/mem"
	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/sysutil"
)

type memoryCollector struct{}

func init() { collector.Register(memoryCollector{}) }

func (memoryCollector) Name() string                      { return "linux/memory" }
func (memoryCollector) Category() string                  { return "memory" }
func (memoryCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

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

	// Slots físicos via dmidecode (requer root). Falha graciosamente.
	if sysutil.CommandExists("dmidecode") {
		collectMemorySlots(ctx, inv)
	}
	return nil
}

var dmiMemBlock = regexp.MustCompile(`(?m)^Memory Device\b`)

func collectMemorySlots(ctx context.Context, inv *inventory.Inventory) {
	out, err := sysutil.RunContext(ctx, "dmidecode", "-t", "17")
	if err != nil {
		return
	}
	// separa em blocos "Memory Device"
	idxs := dmiMemBlock.FindAllStringIndex(out, -1)
	for i, loc := range idxs {
		end := len(out)
		if i+1 < len(idxs) {
			end = idxs[i+1][0]
		}
		block := out[loc[0]:end]
		m := parseMemoryBlock(block)
		// pula slots vazios ("No Module Installed")
		if m.Capacity == 0 && m.SerialNumber == "" {
			continue
		}
		m.NumSlots = i + 1
		inv.AddMemory(m)
	}
}

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

// parseDmiSizeMB converte "16384 MB" / "16 GB" em MB.
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
