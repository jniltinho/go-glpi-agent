//go:build windows

package windows

import (
	"context"
	"runtime"
	"strings"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// usbCollector collects connected USB devices via WMI Win32_PnPEntity, parsing
// the vendor/product id out of the PnP DeviceID. Hubs are skipped.
type usbCollector struct{}

// init registers the usb collector with the collector registry.
func init() { collector.Register(usbCollector{}) }

// Name returns the collector's registry name.
func (usbCollector) Name() string { return "windows/usb" }

// Category returns the inventory section this collector fills.
func (usbCollector) Category() string { return "usb" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (usbCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

type win32PnPEntity struct {
	Name         string
	Manufacturer string
	DeviceID     string
	Service      string
}

// Collect adds one USB device entry per real (non-hub) USB PnP entity.
func (usbCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	var ents []win32PnPEntity
	// The single-quoted LIKE pattern matches PnP ids that start with "USB\".
	if err := queryWMI(`SELECT Name, Manufacturer, DeviceID, Service FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\%'`, &ents); err != nil {
		return err
	}
	for _, e := range ents {
		// skip hubs (root/generic USB hubs)
		if strings.EqualFold(e.Service, "USBHUB") || strings.EqualFold(e.Service, "USBHUB3") ||
			strings.Contains(strings.ToLower(e.Name), "hub") {
			continue
		}
		vid, pid, serial := parseUSBID(e.DeviceID)
		if vid == "" || pid == "" {
			continue
		}
		inv.AddUSBDevice(inventory.USBDevice{
			VendorID:     vid,
			ProductID:    pid,
			Manufacturer: strings.TrimSpace(e.Manufacturer),
			Name:         strings.TrimSpace(e.Name),
			Caption:      strings.TrimSpace(e.Name),
			Serial:       serial,
		})
	}
	return nil
}
