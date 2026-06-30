//go:build freebsd

// Package freebsd holds the FreeBSD-specific inventory collectors. Each collector
// registers itself with the collector registry at init time and runs only when
// the agent is executing on FreeBSD (gated by IsEnabled). Cross-platform signals
// (CPU/mem/disk/net/host/process) come from gopsutil; data gopsutil does not
// expose is read from FreeBSD-native sources: kenv (smbios), pkg, geom/camcontrol,
// sysctl and usbconfig.
//
// Mirrors internal/collector/linux and internal/collector/windows. Pure parsers
// live in parse.go (no build tag) so they are unit-tested on any platform.
package freebsd
