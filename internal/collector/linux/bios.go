package linux

import (
	"context"
	"runtime"
	"strings"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

type biosCollector struct{}

func init() { collector.Register(biosCollector{}) }

func (biosCollector) Name() string                      { return "linux/bios" }
func (biosCollector) Category() string                  { return "bios" }
func (biosCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

const dmiPath = "/sys/class/dmi/id/"

// junkDMI are placeholder DMI strings that mean "no real value". They are
// reported by many BIOSes/VMs (e.g. VirtualBox sets a serial of "0") and must
// not be treated as real data — mirrors what dmidecode/glpi-agent filter out.
var junkDMI = map[string]bool{
	"none": true, "n/a": true, "na": true, "not specified": true,
	"not available": true, "not applicable": true, "default string": true,
	"to be filled by o.e.m.": true, "to be filled by oem": true,
	"system serial number": true, "system product name": true,
	"system manufacturer": true, "system version": true, "system name": true,
	"chassis serial number": true, "base board serial number": true,
	"no asset tag": true, "asset tag": true, "empty": true, "unknown": true,
	"oem": true, "invalid": true, "fill by oem": true,
}

// cleanDMI returns "" for placeholder/junk DMI values (including all-zero
// strings like "0" or "0000"), otherwise the trimmed value.
func cleanDMI(s string) string {
	s = strings.TrimSpace(s)
	if s == "" {
		return ""
	}
	if junkDMI[strings.ToLower(s)] {
		return ""
	}
	if strings.Trim(s, "0") == "" { // "0", "00", "0000000000", ...
		return ""
	}
	return s
}

func (biosCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	// /sys/class/dmi/id/ is readable without root for most fields.
	b := inventory.BIOS{
		SManufacturer: cleanDMI(sysutil.ReadFileTrim(dmiPath + "sys_vendor")),
		SModel:        cleanDMI(sysutil.ReadFileTrim(dmiPath + "product_name")),
		SSN:           cleanDMI(sysutil.ReadFileTrim(dmiPath + "product_serial")),
		BManufacturer: cleanDMI(sysutil.ReadFileTrim(dmiPath + "bios_vendor")),
		BVersion:      sysutil.ReadFileTrim(dmiPath + "bios_version"),
		BDate:         sysutil.ReadFileTrim(dmiPath + "bios_date"),
		AssetTag:      cleanDMI(sysutil.ReadFileTrim(dmiPath + "chassis_asset_tag")),
		MManufacturer: cleanDMI(sysutil.ReadFileTrim(dmiPath + "board_vendor")),
		MModel:        cleanDMI(sysutil.ReadFileTrim(dmiPath + "board_name")),
		MSN:           cleanDMI(sysutil.ReadFileTrim(dmiPath + "board_serial")),
	}
	uuid := sysutil.ReadFileTrim(dmiPath + "product_uuid")

	hasData := b.SManufacturer != "" || b.SModel != "" || b.BVersion != ""
	if !hasData {
		return nil // system without DMI (e.g. VM without passthrough)
	}

	inv.SetBIOS(func(dst *inventory.BIOS) { *dst = b })
	if uuid != "" {
		inv.SetHardware(func(h *inventory.Hardware) { h.UUID = uuid })
	}
	return nil
}
