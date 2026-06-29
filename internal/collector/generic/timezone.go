package generic

import (
	"context"
	"os"
	"strings"
	"time"

	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/sysutil"
)

type timezoneCollector struct{}

func init() { collector.Register(timezoneCollector{}) }

func (timezoneCollector) Name() string                      { return "generic/timezone" }
func (timezoneCollector) Category() string                  { return "timezone" }
func (timezoneCollector) IsEnabled(cfg *config.Config) bool { return true }

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
	return ""
}

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

func twoDigits(n int) string {
	if n < 10 {
		return "0" + string(rune('0'+n))
	}
	return string(rune('0'+n/10)) + string(rune('0'+n%10))
}
