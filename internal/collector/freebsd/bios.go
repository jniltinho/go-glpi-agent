//go:build freebsd

package freebsd

import (
	"context"
	"runtime"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// biosCollector collects BIOS, baseboard and system identity from the smbios.*
// kernel environment variables (the FreeBSD analog of /sys/class/dmi). No root
// required; missing keys degrade gracefully.
type biosCollector struct{}

func init() { collector.Register(biosCollector{}) }

func (biosCollector) Name() string                      { return "freebsd/bios" }
func (biosCollector) Category() string                  { return "bios" }
func (biosCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "freebsd" }

// Collect reads all kenv keys once, maps the smbios.* ones to a BIOS struct, and
// sets the BIOS section plus the hardware UUID. It writes nothing when no real
// identity is present (e.g. a VM whose firmware exposes no SMBIOS).
func (biosCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	out, err := sysutil.RunContext(ctx, "kenv")
	if err != nil {
		return err
	}
	b, uuid := biosFromKenv(parseKenv(out))

	hasData := b.SManufacturer != "" || b.SModel != "" || b.BVersion != "" || b.SSN != ""
	if !hasData && uuid == "" {
		return nil
	}

	// On VirtualBox the smbios serial is "0" (filtered); fall back to the UUID,
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
