//go:build linux

package linux

import (
	"context"
	"runtime"
	"strconv"
	"strings"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// lvmCollector collects LVM logical volumes (name, group, size, UUID) by
// parsing the output of the lvs command.
type lvmCollector struct{}

// init registers the lvm collector with the collector registry.
func init() { collector.Register(lvmCollector{}) }

// Name returns the collector's registry name.
func (lvmCollector) Name() string { return "linux/lvm" }

// Category returns the inventory section this collector fills.
func (lvmCollector) Category() string { return "lvm" }

// IsEnabled reports whether the collector should run; requires Linux and lvs.
func (lvmCollector) IsEnabled(cfg *config.Config) bool {
	return runtime.GOOS == "linux" && sysutil.CommandExists("lvs")
}

// Collect runs lvs with a stable pipe-separated, header-less format and adds one
// volume entry per logical volume, converting the byte size to MB.
func (lvmCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	// Output without header, separated by '|', size in bytes.
	out, err := sysutil.RunContext(ctx, "lvs", "--noheadings", "--units", "b", "--nosuffix",
		"--separator", "|", "-o", "lv_name,vg_name,lv_size,lv_attr,lv_uuid")
	if err != nil {
		return err
	}
	for _, line := range sysutil.SplitLines(out) {
		fields := strings.Split(strings.TrimSpace(line), "|")
		if len(fields) < 5 {
			continue
		}
		sizeBytes, _ := strconv.ParseInt(strings.TrimSpace(fields[2]), 10, 64)
		inv.AddVolume(inventory.Volume{
			LVName: strings.TrimSpace(fields[0]),
			VGName: strings.TrimSpace(fields[1]),
			Size:   int(sizeBytes / 1024 / 1024),
			Attr:   strings.TrimSpace(fields[3]),
			LVUUID: strings.TrimSpace(fields[4]),
		})
	}
	return nil
}
