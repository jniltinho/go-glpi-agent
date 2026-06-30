//go:build windows

package windows

import (
	"context"
	"runtime"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// biosCollector collects BIOS, baseboard and system identity (vendor, model,
// serials, asset tag, UUID) via WMI. All identity strings are run through the
// shared junk filter so placeholder values are not reported as real data.
type biosCollector struct{}

// init registers the bios collector with the collector registry.
func init() { collector.Register(biosCollector{}) }

// Name returns the collector's registry name.
func (biosCollector) Name() string { return "windows/bios" }

// Category returns the inventory section this collector fills.
func (biosCollector) Category() string { return "bios" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (biosCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

type win32BIOS struct {
	Manufacturer      string
	SMBIOSBIOSVersion string
	SerialNumber      string
	ReleaseDate       string // CIM_DATETIME, e.g. 20210510000000.000000+000
}

type win32ComputerSystem struct {
	Manufacturer string
	Model        string
}

type win32BaseBoard struct {
	Manufacturer string
	Product      string
	SerialNumber string
}

type win32SystemEnclosure struct {
	SerialNumber   string
	SMBIOSAssetTag string
}

type win32ComputerSystemProduct struct {
	UUID              string
	IdentifyingNumber string
}

// Collect reads the WMI identity classes into a BIOS struct and sets the BIOS
// section plus the hardware UUID. It returns without writing when no real
// identity is present (e.g. a VM without SMBIOS passthrough).
func (biosCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	b := inventory.BIOS{}

	var bios []win32BIOS
	if err := queryWMI("SELECT Manufacturer, SMBIOSBIOSVersion, SerialNumber, ReleaseDate FROM Win32_BIOS", &bios); err == nil && len(bios) > 0 {
		b.BManufacturer = sysutil.CleanDMI(bios[0].Manufacturer)
		b.BVersion = bios[0].SMBIOSBIOSVersion
		b.BDate = cimDate(bios[0].ReleaseDate)
		if b.SSN == "" {
			b.SSN = sysutil.CleanDMI(bios[0].SerialNumber)
		}
	}

	var cs []win32ComputerSystem
	if err := queryWMI("SELECT Manufacturer, Model FROM Win32_ComputerSystem", &cs); err == nil && len(cs) > 0 {
		b.SManufacturer = sysutil.CleanDMI(cs[0].Manufacturer)
		b.SModel = sysutil.CleanDMI(cs[0].Model)
	}

	var board []win32BaseBoard
	if err := queryWMI("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard", &board); err == nil && len(board) > 0 {
		b.MManufacturer = sysutil.CleanDMI(board[0].Manufacturer)
		b.MModel = sysutil.CleanDMI(board[0].Product)
		b.MSN = sysutil.CleanDMI(board[0].SerialNumber)
	}

	var enc []win32SystemEnclosure
	if err := queryWMI("SELECT SerialNumber, SMBIOSAssetTag FROM Win32_SystemEnclosure", &enc); err == nil && len(enc) > 0 {
		b.AssetTag = sysutil.CleanDMI(enc[0].SMBIOSAssetTag)
		if b.SSN == "" {
			b.SSN = sysutil.CleanDMI(enc[0].SerialNumber)
		}
	}

	uuid := ""
	var prod []win32ComputerSystemProduct
	if err := queryWMI("SELECT UUID, IdentifyingNumber FROM Win32_ComputerSystemProduct", &prod); err == nil && len(prod) > 0 {
		uuid = sysutil.CleanDMI(prod[0].UUID)
		if b.SSN == "" {
			b.SSN = sysutil.CleanDMI(prod[0].IdentifyingNumber)
		}
	}

	hasData := b.SManufacturer != "" || b.SModel != "" || b.BVersion != "" || b.SSN != ""
	if !hasData && uuid == "" {
		return nil
	}

	// On VirtualBox the WMI serial is "0" (filtered); fall back to the UUID,
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
