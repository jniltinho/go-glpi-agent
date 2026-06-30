//go:build darwin

package macos

import (
	"context"
	"runtime"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// biosCollector collects the system identity (manufacturer, model, serial,
// boot-ROM version) and the hardware UUID. It mirrors the official agent's
// MacOS/Bios.pm + MacOS/Hardware.pm: read system_profiler SPHardwareDataType,
// fall back to ioreg's IOPlatformExpertDevice for any missing field, and — so a
// host is never reported without a serial — fall back to the UUID as the serial.
type biosCollector struct{}

func init() { collector.Register(biosCollector{}) }

func (biosCollector) Name() string                      { return "macos/bios" }
func (biosCollector) Category() string                  { return "bios" }
func (biosCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "darwin" }

// Collect resolves identity through the documented fallback chain and writes the
// BIOS section plus HARDWARE.UUID. It returns without writing identity only when
// neither a serial nor a UUID can be obtained.
func (biosCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	hw := systemProfilerHardware(ctx)
	io := ioregPlatform(ctx) // fallback source

	b, uuid := resolveIdentity(hw, io)

	if b.SSN == "" && uuid == "" && b.SModel == "" {
		return nil // no identity available at all
	}

	inv.SetBIOS(func(dst *inventory.BIOS) { *dst = b })
	if uuid != "" {
		inv.SetHardware(func(h *inventory.Hardware) {
			if h.UUID == "" {
				h.UUID = uuid
			}
		})
	}
	return nil
}
