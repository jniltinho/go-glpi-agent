package linux

import (
	"context"
	"runtime"

	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/sysutil"
)

type biosCollector struct{}

func init() { collector.Register(biosCollector{}) }

func (biosCollector) Name() string                      { return "linux/bios" }
func (biosCollector) Category() string                  { return "bios" }
func (biosCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

const dmiPath = "/sys/class/dmi/id/"

func (biosCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	// /sys/class/dmi/id/ é legível sem root para a maioria dos campos.
	b := inventory.BIOS{
		SManufacturer: sysutil.ReadFileTrim(dmiPath + "sys_vendor"),
		SModel:        sysutil.ReadFileTrim(dmiPath + "product_name"),
		SSN:           sysutil.ReadFileTrim(dmiPath + "product_serial"),
		BManufacturer: sysutil.ReadFileTrim(dmiPath + "bios_vendor"),
		BVersion:      sysutil.ReadFileTrim(dmiPath + "bios_version"),
		BDate:         sysutil.ReadFileTrim(dmiPath + "bios_date"),
		AssetTag:      sysutil.ReadFileTrim(dmiPath + "chassis_asset_tag"),
		MManufacturer: sysutil.ReadFileTrim(dmiPath + "board_vendor"),
		MModel:        sysutil.ReadFileTrim(dmiPath + "board_name"),
		MSN:           sysutil.ReadFileTrim(dmiPath + "board_serial"),
	}
	uuid := sysutil.ReadFileTrim(dmiPath + "product_uuid")

	hasData := b.SManufacturer != "" || b.SModel != "" || b.BVersion != ""
	if !hasData {
		return nil // sistema sem DMI (ex: VM sem passthrough)
	}

	inv.SetBIOS(func(dst *inventory.BIOS) { *dst = b })
	if uuid != "" {
		inv.SetHardware(func(h *inventory.Hardware) { h.UUID = uuid })
	}
	return nil
}
