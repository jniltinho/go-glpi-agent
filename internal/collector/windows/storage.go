//go:build windows

package windows

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

// storageCollector collects physical disks via WMI Win32_DiskDrive.
type storageCollector struct{}

// init registers the storage collector with the collector registry.
func init() { collector.Register(storageCollector{}) }

// Name returns the collector's registry name.
func (storageCollector) Name() string { return "windows/storages" }

// Category returns the inventory section this collector fills.
func (storageCollector) Category() string { return "storage" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (storageCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

type win32DiskDrive struct {
	Model            string
	SerialNumber     string
	Size             uint64
	InterfaceType    string
	FirmwareRevision string
	MediaType        string
	PNPDeviceID      string
}

// Collect adds one storage entry per physical disk reported by Win32_DiskDrive.
// ponytail: NVMe is detected by name; SSD-vs-HDD discrimination would need a
// MSFT_PhysicalDisk query in the Storage namespace — add that if GLPI needs the
// exact media type.
func (storageCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	var disks []win32DiskDrive
	if err := queryWMI("SELECT Model, SerialNumber, Size, InterfaceType, FirmwareRevision, MediaType, PNPDeviceID FROM Win32_DiskDrive", &disks); err != nil {
		return err
	}
	for _, d := range disks {
		inv.AddStorage(inventory.Storage{
			Name:         strings.TrimSpace(d.Model),
			Model:        strings.TrimSpace(d.Model),
			Type:         diskType(d.Model, d.PNPDeviceID, d.MediaType),
			DiskSize:     int(d.Size / 1024 / 1024),
			SerialNumber: sysutil.CleanDMI(strings.TrimSpace(d.SerialNumber)),
			Firmware:     strings.TrimSpace(d.FirmwareRevision),
			Description:  strings.TrimSpace(d.InterfaceType),
		})
	}
	return nil
}

// diskType classifies a disk as NVMe (by name/PnP id) or falls back to the
// WMI MediaType string, defaulting to "disk".
func diskType(model, pnp, media string) string {
	hay := strings.ToLower(model + " " + pnp)
	if strings.Contains(hay, "nvme") {
		return "NVMe"
	}
	if m := strings.TrimSpace(media); m != "" {
		return m
	}
	return "disk"
}

// --- logical drives / filesystems via gopsutil/disk ---

// driveCollector collects mounted volumes (drive letters) and their usage.
type driveCollector struct{}

// init registers the drive collector with the collector registry.
func init() { collector.Register(driveCollector{}) }

// Name returns the collector's registry name.
func (driveCollector) Name() string { return "windows/drives" }

// Category returns the inventory section this collector fills.
func (driveCollector) Category() string { return "drive" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (driveCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

// Collect adds one drive entry per mounted volume with total/free space.
func (driveCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	parts, err := disk.PartitionsWithContext(ctx, false)
	if err != nil {
		return err
	}
	for _, p := range parts {
		if p.Fstype == "" {
			continue
		}
		d := inventory.Drive{
			Volumn:     p.Device, // e.g. "C:"
			Type:       p.Mountpoint,
			FileSystem: p.Fstype, // e.g. "NTFS"
		}
		if usage, uerr := disk.UsageWithContext(ctx, p.Mountpoint); uerr == nil {
			d.Total = int(usage.Total / 1024 / 1024)
			d.Free = int(usage.Free / 1024 / 1024)
		}
		inv.AddDrive(d)
	}
	return nil
}
