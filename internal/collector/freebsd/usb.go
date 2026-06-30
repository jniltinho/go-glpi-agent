//go:build freebsd

package freebsd

import (
	"context"
	"runtime"
	"strings"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// usbCollector collects connected USB devices via usbconfig. Best-effort: when
// usbconfig is absent or returns nothing, no devices are added.
type usbCollector struct{}

func init() { collector.Register(usbCollector{}) }

func (usbCollector) Name() string     { return "freebsd/usb" }
func (usbCollector) Category() string { return "usb" }
func (usbCollector) IsEnabled(cfg *config.Config) bool {
	return runtime.GOOS == "freebsd" && sysutil.CommandExists("usbconfig")
}

// Collect lists USB devices and dumps each device descriptor, adding the non-hub
// ones. Device ids are read from the `idVendor`/`idProduct` lines of
// `usbconfig -d <dev> dump_device_desc`.
func (usbCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	list, err := sysutil.RunContext(ctx, "usbconfig", "list")
	if err != nil {
		return nil
	}
	for _, line := range sysutil.SplitLines(list) {
		dev, _, ok := strings.Cut(line, ":")
		dev = strings.TrimSpace(dev)
		if !ok || dev == "" {
			continue
		}
		out, derr := sysutil.RunContext(ctx, "usbconfig", "-d", dev, "dump_device_desc")
		if derr != nil {
			continue
		}
		u, isHub := parseUSBDesc(out)
		if isHub || u.VendorID == "" || u.ProductID == "" {
			continue
		}
		inv.AddUSBDevice(u)
	}
	return nil
}
