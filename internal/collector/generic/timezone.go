package generic

import (
	"context"
	"os"
	"strings"
	"time"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// timezoneCollector records the system timezone name and its current UTC
// offset in the operating-system section.
type timezoneCollector struct{}

// init registers the timezone collector with the collector registry.
func init() { collector.Register(timezoneCollector{}) }

// Name returns the collector identifier.
func (timezoneCollector) Name() string { return "generic/timezone" }

// Category returns the inventory category controlled by --no-category.
func (timezoneCollector) Category() string { return "timezone" }

// IsEnabled always returns true; timezone is collected on every host.
func (timezoneCollector) IsEnabled(cfg *config.Config) bool { return true }

// Collect resolves the timezone name and computes the offset from the current
// local time, so DST is reflected as of the moment of collection.
func (timezoneCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	name := timezoneName()
	// offset atual no formato +HHMM
	_, offsetSec := time.Now().Zone()
	offset := formatOffset(offsetSec)

	inv.SetOperatingSystem(func(o *inventory.OperatingSystem) {
		o.TimezoneName = name
		o.TimezoneUTCO = offset
	})
	return nil
}

// timezoneName returns the IANA zone name, preferring /etc/timezone and
// falling back to the /etc/localtime symlink target; empty if neither is found.
func timezoneName() string {
	// 1) /etc/timezone (Debian/Ubuntu)
	if tz := sysutil.ReadFileTrim("/etc/timezone"); tz != "" {
		return tz
	}
	// 2) symlink /etc/localtime -> .../zoneinfo/Area/City
	if target, err := os.Readlink("/etc/localtime"); err == nil {
		if i := strings.Index(target, "zoneinfo/"); i >= 0 {
			return target[i+len("zoneinfo/"):]
		}
	}
	// 3) OS-specific source (Windows registry); "" on Unix.
	return osTimezoneName()
}

// formatOffset renders a signed second offset as the GLPI-expected +HHMM/-HHMM
// string. Seconds beyond whole minutes are truncated.
func formatOffset(sec int) string {
	sign := "+"
	if sec < 0 {
		sign = "-"
		sec = -sec
	}
	h := sec / 3600
	m := (sec % 3600) / 60
	return sign + twoDigits(h) + twoDigits(m)
}

// twoDigits zero-pads n to two characters; assumes 0 <= n < 100.
func twoDigits(n int) string {
	if n < 10 {
		return "0" + string(rune('0'+n))
	}
	return string(rune('0'+n/10)) + string(rune('0'+n%10))
}
