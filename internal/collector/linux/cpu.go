package linux

import (
	"context"
	"runtime"
	"strconv"

	"github.com/shirou/gopsutil/v3/cpu"
	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
)

type cpuCollector struct{}

func init() { collector.Register(cpuCollector{}) }

func (cpuCollector) Name() string     { return "linux/cpu" }
func (cpuCollector) Category() string { return "cpu" }

func (cpuCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

func (cpuCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	infos, err := cpu.InfoWithContext(ctx)
	if err != nil {
		return err
	}

	physical, _ := cpu.CountsWithContext(ctx, false)
	logical, _ := cpu.CountsWithContext(ctx, true)

	// gopsutil retorna uma entrada por core lógico. Agrupamos por physical id
	// para representar sockets físicos, como o GLPI espera.
	type sock struct {
		info    cpu.InfoStat
		cores   map[string]bool
		threads int // total de processadores lógicos no socket
	}
	socks := map[string]*sock{}
	var order []string
	for _, c := range infos {
		key := c.PhysicalID
		if key == "" {
			key = c.VendorID + "|" + c.ModelName
		}
		s, ok := socks[key]
		if !ok {
			s = &sock{info: c, cores: map[string]bool{}}
			socks[key] = s
			order = append(order, key)
		}
		s.cores[c.CoreID] = true
		s.threads++
	}

	if len(order) == 0 {
		// fallback: um único CPU lógico
		inv.AddCPU(inventory.CPU{
			Arch:      runtime.GOARCH,
			CoreCount: logical,
			Core:      physical,
		})
		return nil
	}

	for _, key := range order {
		s := socks[key]
		coreCount := len(s.cores)
		if coreCount == 0 {
			coreCount = 1
		}
		inv.AddCPU(inventory.CPU{
			Name:         s.info.ModelName,
			Manufacturer: normalizeVendor(s.info.VendorID),
			Speed:        int(s.info.Mhz),
			Core:         coreCount,
			Thread:       s.threads, // total de threads no socket (igual ao Perl)
			CoreCount:    s.threads,
			Arch:         runtime.GOARCH,
			ID:           s.info.PhysicalID,
			FamilyNumber: s.info.Family,
			Model:        s.info.Model,
			Stepping:     strconv.Itoa(int(s.info.Stepping)),
		})
	}
	return nil
}

// normalizeVendor traduz o vendor_id do /proc/cpuinfo para o nome usado pelo
// agente Perl/GLPI.
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
