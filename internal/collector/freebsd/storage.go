//go:build freebsd

package freebsd

import (
	"context"
	"runtime"
	"strings"

	"github.com/shirou/gopsutil/v3/disk"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// storageCollector collects physical disks via `geom disk list`.
type storageCollector struct{}

func init() { collector.Register(storageCollector{}) }

func (storageCollector) Name() string     { return "freebsd/storages" }
func (storageCollector) Category() string { return "storage" }
func (storageCollector) IsEnabled(cfg *config.Config) bool {
	return runtime.GOOS == "freebsd" && sysutil.CommandExists("geom")
}

// Collect adds one storage entry per disk reported by `geom disk list`.
func (storageCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	out, err := sysutil.RunContext(ctx, "geom", "disk", "list")
	if err != nil {
		return err
	}
	for _, d := range parseGeomDiskList(out) {
		typ := "disk"
		if strings.HasPrefix(d.Name, "nvd") || strings.HasPrefix(d.Name, "nvme") {
			typ = "NVMe"
		}
		inv.AddStorage(inventory.Storage{
			Name:         "/dev/" + d.Name,
			Model:        d.Descr,
			Type:         typ,
			DiskSize:     int(d.Mediasize / 1024 / 1024),
			SerialNumber: sysutil.CleanDMI(d.Ident),
		})
	}
	return nil
}

// --- filesystems via gopsutil/disk ---

// driveCollector collects mounted UFS/ZFS filesystems and their usage.
type driveCollector struct{}

func init() { collector.Register(driveCollector{}) }

func (driveCollector) Name() string                      { return "freebsd/drives" }
func (driveCollector) Category() string                  { return "drive" }
func (driveCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "freebsd" }

// pseudoFS are virtual FreeBSD filesystems that hold no user data.
var pseudoFS = map[string]bool{
	"devfs": true, "procfs": true, "fdescfs": true, "linprocfs": true,
	"linsysfs": true, "tmpfs": true, "nullfs": true, "fdesc": true,
}

// Collect adds one drive entry per real (non-pseudo) mounted filesystem.
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
			FileSystem: p.Fstype, // ufs, zfs
		}
		if usage, uerr := disk.UsageWithContext(ctx, p.Mountpoint); uerr == nil {
			d.Total = int(usage.Total / 1024 / 1024)
			d.Free = int(usage.Free / 1024 / 1024)
		}
		inv.AddDrive(d)
	}
	return nil
}
