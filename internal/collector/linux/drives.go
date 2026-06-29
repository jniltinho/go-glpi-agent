package linux

import (
	"context"
	"encoding/json"
	"runtime"
	"strconv"
	"strings"

	"github.com/shirou/gopsutil/v3/disk"
	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/sysutil"
)

type driveCollector struct{}

func init() { collector.Register(driveCollector{}) }

func (driveCollector) Name() string                      { return "linux/drives" }
func (driveCollector) Category() string                  { return "drive" }
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

type storageCollector struct{}

func init() { collector.Register(storageCollector{}) }

func (storageCollector) Name() string     { return "linux/storages" }
func (storageCollector) Category() string { return "storage" }
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

func (f *flexBool) UnmarshalJSON(b []byte) error {
	s := strings.Trim(string(b), `"`)
	*f = flexBool(s == "1" || s == "true")
	return nil
}

type lsblkOutput struct {
	BlockDevices []lsblkDevice `json:"blockdevices"`
}

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
