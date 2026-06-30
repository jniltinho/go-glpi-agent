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

// init registers the process collector with the collector registry.
func init() { collector.Register(processCollector{}) }

// Name returns the collector identifier.
func (processCollector) Name() string { return "generic/processes" }

// Category returns the inventory category controlled by --no-category.
func (processCollector) Category() string { return "process" }

// IsEnabled gates the collector on the scan-processes option, which is off by
// default because enumerating every process is expensive.
func (processCollector) IsEnabled(cfg *config.Config) bool { return cfg.ScanProcesses }

// Collect enumerates running processes via gopsutil, tolerating per-field
// errors (name, user, cpu, mem, cmdline, start time) so a single inaccessible
// process does not abort the whole scan. VMS is reported in KB.
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
