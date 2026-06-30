//go:build darwin

package macos

import (
	"context"
	"runtime"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// usbCollector collects connected USB devices via `system_profiler SPUSBDataType`,
// recursing through the hub tree and skipping hubs.
type usbCollector struct{}

func init() { collector.Register(usbCollector{}) }

func (usbCollector) Name() string                      { return "macos/usb" }
func (usbCollector) Category() string                  { return "usb" }
func (usbCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "darwin" }

// Collect adds one USB device entry per non-hub device.
func (usbCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	for _, u := range parseSPUSB(systemProfilerJSON(ctx, "SPUSBDataType")) {
		inv.AddUSBDevice(u)
	}
	return nil
}
