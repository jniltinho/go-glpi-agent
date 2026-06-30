//go:build linux

package linux

import (
	"context"
	"encoding/json"
	"runtime"
	"strconv"
	"strings"

	"github.com/shirou/gopsutil/v3/disk"
	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// driveCollector collects mounted filesystems (volumes) and their usage via
// gopsutil/disk, skipping virtual kernel filesystems.
type driveCollector struct{}

// init registers the drive collector with the collector registry.
func init() { collector.Register(driveCollector{}) }

// Name returns the collector's registry name.
func (driveCollector) Name() string { return "linux/drives" }

// Category returns the inventory section this collector fills.
func (driveCollector) Category() string { return "drive" }

// IsEnabled reports whether the collector should run; it is Linux-only.
func (driveCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

// pseudoFS are virtual kernel filesystems that hold no user data.
// overlay/9p/rootfs/squashfs are NOT listed here because they represent
// real mounts (and the Perl agent reports them).
var pseudoFS = map[string]bool{
	"proc": true, "sysfs": true, "devtmpfs": true, "devpts": true,
	"tmpfs": true, "cgroup": true, "cgroup2": true, "pstore": true,
	"securityfs": true, "debugfs": true, "tracefs": true, "mqueue": true,
	"hugetlbfs": true, "bpf": true, "configfs": true, "fusectl": true,
	"binfmt_misc": true, "autofs": true, "nsfs": true,
}

// Collect adds one drive entry per real (non-pseudo) mounted filesystem,
// filling total/free space when usage stats are available for the mountpoint.
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
			FileSystem: p.Fstype,
		}
		if usage, uerr := disk.UsageWithContext(ctx, p.Mountpoint); uerr == nil {
			d.Total = int(usage.Total / 1024 / 1024)
			d.Free = int(usage.Free / 1024 / 1024)
		}
		inv.AddDrive(d)
	}
	return nil
}

// --- physical disks via lsblk (STORAGES) ---

// storageCollector collects physical disks (model, serial, size, type) by
// parsing lsblk's JSON output.
type storageCollector struct{}

// init registers the storage collector with the collector registry.
func init() { collector.Register(storageCollector{}) }

// Name returns the collector's registry name.
func (storageCollector) Name() string { return "linux/storages" }

// Category returns the inventory section this collector fills.
func (storageCollector) Category() string { return "storage" }

// IsEnabled reports whether the collector should run; requires Linux and lsblk.
func (storageCollector) IsEnabled(cfg *config.Config) bool {
	return runtime.GOOS == "linux" && sysutil.CommandExists("lsblk")
}

type lsblkDevice struct {
	Name   string    `json:"name"`
	Type   string    `json:"type"`
	Size   flexInt64 `json:"size"`
	Model  string    `json:"model"`
	Serial string    `json:"serial"`
	Vendor string    `json:"vendor"`
	Rota   flexBool  `json:"rota"`
	Rev    string    `json:"rev"`
	WWN    string    `json:"wwn"`
}

// flexInt64 / flexBool tolerate lsblk JSON from older util-linux (< 2.33, e.g.
// AlmaLinux/Oracle 8), which emits every value as a quoted string ("107..."),
// as well as newer lsblk, which emits real numbers/booleans.
type flexInt64 int64

// UnmarshalJSON accepts both quoted strings and bare JSON numbers, returning 0
// for empty/null/non-numeric values rather than failing.
func (f *flexInt64) UnmarshalJSON(b []byte) error {
	s := strings.Trim(string(b), `"`)
	if s == "" || s == "null" {
		return nil
	}
	n, err := strconv.ParseInt(s, 10, 64)
	if err != nil {
		return nil // non-numeric (e.g. a size with a unit suffix): best-effort 0
	}
	*f = flexInt64(n)
	return nil
}

type flexBool bool

// UnmarshalJSON treats "1"/"true" (quoted or bare) as true, anything else as
// false, tolerating both old and new lsblk JSON encodings.
func (f *flexBool) UnmarshalJSON(b []byte) error {
	s := strings.Trim(string(b), `"`)
	*f = flexBool(s == "1" || s == "true")
	return nil
}

type lsblkOutput struct {
	BlockDevices []lsblkDevice `json:"blockdevices"`
}

// Collect runs lsblk for top-level block devices and adds one storage entry per
// disk, classifying each as NVMe, SSD or HDD from its name and rotational flag.
func (storageCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	out, err := sysutil.RunContext(ctx, "lsblk", "-d", "-b", "-J",
		"-o", "NAME,TYPE,SIZE,MODEL,SERIAL,VENDOR,ROTA,REV,WWN")
	if err != nil {
		return err
	}
	var parsed lsblkOutput
	if err := json.Unmarshal([]byte(out), &parsed); err != nil {
		return err
	}
	for _, d := range parsed.BlockDevices {
		if d.Type != "disk" {
			continue
		}
		typ := "SSD"
		if bool(d.Rota) {
			typ = "HDD"
		}
		if strings.HasPrefix(d.Name, "nvme") {
			typ = "NVMe"
		}
		inv.AddStorage(inventory.Storage{
			Name:         "/dev/" + d.Name,
			Manufacturer: strings.TrimSpace(d.Vendor),
			Model:        strings.TrimSpace(d.Model),
			Type:         typ,
			DiskSize:     int(int64(d.Size) / 1024 / 1024),
			SerialNumber: strings.TrimSpace(d.Serial),
			Firmware:     strings.TrimSpace(d.Rev),
			WWN:          strings.TrimSpace(d.WWN),
		})
	}
	return nil
}
