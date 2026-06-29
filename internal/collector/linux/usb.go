package linux

import (
	"context"
	"os"
	"path/filepath"
	"runtime"

	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/sysutil"
)

type usbCollector struct{}

func init() { collector.Register(usbCollector{}) }

func (usbCollector) Name() string                      { return "linux/usb" }
func (usbCollector) Category() string                  { return "usb" }
func (usbCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

const usbDevPath = "/sys/bus/usb/devices"

func (usbCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	entries, err := os.ReadDir(usbDevPath)
	if err != nil {
		return err
	}
	for _, e := range entries {
		base := filepath.Join(usbDevPath, e.Name())
		// idVendor existe apenas em devices (não em interfaces nem barramentos).
		vendor := sysutil.ReadFileTrim(filepath.Join(base, "idVendor"))
		product := sysutil.ReadFileTrim(filepath.Join(base, "idProduct"))
		if vendor == "" || product == "" {
			continue
		}
		class := sysutil.ReadFileTrim(filepath.Join(base, "bDeviceClass"))
		// pula hubs (classe 09)
		if class == "09" {
			continue
		}
		inv.AddUSBDevice(inventory.USBDevice{
			VendorID:     vendor,
			ProductID:    product,
			Manufacturer: sysutil.ReadFileTrim(filepath.Join(base, "manufacturer")),
			Name:         sysutil.ReadFileTrim(filepath.Join(base, "product")),
			Caption:      sysutil.ReadFileTrim(filepath.Join(base, "product")),
			Serial:       sysutil.ReadFileTrim(filepath.Join(base, "serial")),
			Class:        class,
			SubClass:     sysutil.ReadFileTrim(filepath.Join(base, "bDeviceSubClass")),
		})
	}
	return nil
}
