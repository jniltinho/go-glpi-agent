//go:build linux

package linux

import (
	"context"
	"runtime"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// biosCollector collects BIOS, baseboard and system identity (vendor, model,
// serials, asset tag, UUID) by reading /sys/class/dmi/id/, no root required.
type biosCollector struct{}

// init registers the bios collector with the collector registry.
func init() { collector.Register(biosCollector{}) }

// Name returns the collector's registry name.
func (biosCollector) Name() string { return "linux/bios" }

// Category returns the inventory section this collector fills.
func (biosCollector) Category() string { return "bios" }

// IsEnabled reports whether the collector should run; it is Linux-only.
func (biosCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

const dmiPath = "/sys/class/dmi/id/"

// cleanDMI filters placeholder/junk DMI values; the implementation is shared
// with the Windows WMI collectors via sysutil.CleanDMI.
func cleanDMI(s string) string { return sysutil.CleanDMI(s) }

// Collect reads the DMI fields into a BIOS struct and sets the BIOS section
// plus the hardware UUID. It returns without writing anything when no real DMI
// data is present (e.g. a VM without SMBIOS passthrough).
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

	// On VirtualBox the DMI serial is "0" (filtered above); fall back to the UUID,
	// matching glpi-agent, so the host still gets a stable serial in GLPI.
	if ssn := sysutil.VirtualBoxSerial(b.SSN, b.MSN, b.MModel, uuid); ssn != "" {
		b.SSN = ssn
	}

	inv.SetBIOS(func(dst *inventory.BIOS) { *dst = b })
	if uuid != "" {
		inv.SetHardware(func(h *inventory.Hardware) { h.UUID = uuid })
	}
	return nil
}
