//go:build linux

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

// usbCollector collects connected USB devices by walking /sys/bus/usb/devices.
type usbCollector struct{}

// init registers the usb collector with the collector registry.
func init() { collector.Register(usbCollector{}) }

// Name returns the collector's registry name.
func (usbCollector) Name() string { return "linux/usb" }

// Category returns the inventory section this collector fills.
func (usbCollector) Category() string { return "usb" }

// IsEnabled reports whether the collector should run; it is Linux-only.
func (usbCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

const usbDevPath = "/sys/bus/usb/devices"

// Collect adds one USB device entry per real device under usbDevPath. Sysfs
// entries that are interfaces/buses (no idVendor/idProduct) and hubs (class 09)
// are skipped.
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
