//go:build darwin

package macos

import (
	"context"
	"runtime"

	"github.com/shirou/gopsutil/v3/disk"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// storageCollector collects physical disks via system_profiler (SPNVMeDataType
// for Apple/NVMe SSDs and SPSerialATADataType for SATA drives).
type storageCollector struct{}

func init() { collector.Register(storageCollector{}) }

func (storageCollector) Name() string                      { return "macos/storages" }
func (storageCollector) Category() string                  { return "storage" }
func (storageCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "darwin" }

// Collect adds one storage entry per physical disk reported by system_profiler.
func (storageCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	for _, s := range parseSPStorage(systemProfilerJSON(ctx, "SPNVMeDataType"), "SPNVMeDataType", "NVMe") {
		inv.AddStorage(s)
	}
	for _, s := range parseSPStorage(systemProfilerJSON(ctx, "SPSerialATADataType"), "SPSerialATADataType", "SATA") {
		inv.AddStorage(s)
	}
	return nil
}

// --- filesystems via gopsutil/disk ---

// driveCollector collects mounted (APFS/HFS) filesystems and their usage.
type driveCollector struct{}

func init() { collector.Register(driveCollector{}) }

func (driveCollector) Name() string                      { return "macos/drives" }
func (driveCollector) Category() string                  { return "drive" }
func (driveCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "darwin" }

// pseudoFS are virtual macOS filesystems that hold no user data.
var pseudoFS = map[string]bool{
	"devfs": true, "autofs": true, "tmpfs": true, "nullfs": true,
}

// Collect adds one drive entry per real (non-pseudo) mounted filesystem. The
// synthetic read-only system snapshot and other pseudo mounts are skipped.
func (driveCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	parts, err := disk.PartitionsWithContext(ctx, true)
	if err != nil {
		return err
	}
	for _, p := range parts {
		if pseudoFS[p.Fstype] {
			continue
		}
		d := inventory.Drive{
			Volumn:     p.Device,
			Type:       p.Mountpoint,
			FileSystem: p.Fstype, // apfs, hfs, ...
		}
		if usage, uerr := disk.UsageWithContext(ctx, p.Mountpoint); uerr == nil {
			d.Total = int(usage.Total / 1024 / 1024)
			d.Free = int(usage.Free / 1024 / 1024)
		}
		inv.AddDrive(d)
	}
	return nil
}
