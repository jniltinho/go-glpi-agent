package linux

import (
	"context"
	"encoding/json"
	"runtime"
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

// pseudoFS são filesystems virtuais do kernel que não carregam dados de
// usuário. overlay/9p/rootfs/squashfs NÃO entram aqui pois representam
// montagens reais (e o agente Perl as reporta).
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

// --- discos físicos via lsblk (STORAGES) ---

type storageCollector struct{}

func init() { collector.Register(storageCollector{}) }

func (storageCollector) Name() string     { return "linux/storages" }
func (storageCollector) Category() string { return "storage" }
func (storageCollector) IsEnabled(cfg *config.Config) bool {
	return runtime.GOOS == "linux" && sysutil.CommandExists("lsblk")
}

type lsblkDevice struct {
	Name   string `json:"name"`
	Type   string `json:"type"`
	Size   int64  `json:"size"`
	Model  string `json:"model"`
	Serial string `json:"serial"`
	Vendor string `json:"vendor"`
	Rota   bool   `json:"rota"`
	Rev    string `json:"rev"`
	WWN    string `json:"wwn"`
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
		if d.Rota {
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
			DiskSize:     int(d.Size / 1024 / 1024),
			SerialNumber: strings.TrimSpace(d.Serial),
			Firmware:     strings.TrimSpace(d.Rev),
			WWN:          strings.TrimSpace(d.WWN),
		})
	}
	return nil
}
