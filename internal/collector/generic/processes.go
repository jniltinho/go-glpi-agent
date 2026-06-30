package generic

import (
	"context"
	"time"

	"github.com/shirou/gopsutil/v3/process"
	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// processCollector only runs when scan-processes=1 (disabled by default, as in
// the Perl agent).
type processCollector struct{}

func init() { collector.Register(processCollector{}) }

func (processCollector) Name() string     { return "generic/processes" }
func (processCollector) Category() string { return "process" }

func (processCollector) IsEnabled(cfg *config.Config) bool { return cfg.ScanProcesses }

func (processCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	procs, err := process.ProcessesWithContext(ctx)
	if err != nil {
		return err
	}
	for _, p := range procs {
		name, _ := p.NameWithContext(ctx)
		username, _ := p.UsernameWithContext(ctx)
		cpuPct, _ := p.CPUPercentWithContext(ctx)
		memPct, _ := p.MemoryPercentWithContext(ctx)
		cmdline, _ := p.CmdlineWithContext(ctx)
		if cmdline == "" {
			cmdline = name
		}

		started := ""
		if ms, e := p.CreateTimeWithContext(ctx); e == nil {
			started = time.Unix(ms/1000, 0).Format("2006-01-02 15:04")
		}

		var vmem uint64
		if mi, e := p.MemoryInfoWithContext(ctx); e == nil && mi != nil {
			vmem = mi.VMS / 1024 // KB
		}

		inv.AddProcess(inventory.Process{
			User:          username,
			PID:           p.Pid,
			CPUUsage:      cpuPct,
			Mem:           memPct,
			VirtualMemory: vmem,
			Started:       started,
			Cmd:           cmdline,
		})
	}
	return nil
}
