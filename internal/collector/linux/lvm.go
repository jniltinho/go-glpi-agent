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

type lvmCollector struct{}

func init() { collector.Register(lvmCollector{}) }

func (lvmCollector) Name() string     { return "linux/lvm" }
func (lvmCollector) Category() string { return "lvm" }
func (lvmCollector) IsEnabled(cfg *config.Config) bool {
	return runtime.GOOS == "linux" && sysutil.CommandExists("lvs")
}

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
