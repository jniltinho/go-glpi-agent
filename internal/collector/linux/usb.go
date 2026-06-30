package linux

import (
	"context"
	"os"
	"path/filepath"
	"runtime"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
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
		// idVendor exists only on devices (not on interfaces or buses).
		vendor := sysutil.ReadFileTrim(filepath.Join(base, "idVendor"))
		product := sysutil.ReadFileTrim(filepath.Join(base, "idProduct"))
		if vendor == "" || product == "" {
			continue
		}
		class := sysutil.ReadFileTrim(filepath.Join(base, "bDeviceClass"))
		// skip hubs (class 09)
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
