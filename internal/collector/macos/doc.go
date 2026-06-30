//go:build darwin

// Package macos holds the macOS-specific inventory collectors. Each collector
// registers itself with the collector registry at init time and runs only when
// the agent is executing on macOS (gated by IsEnabled). Cross-platform signals
// (CPU/mem/disk/net/host/process) come from gopsutil; data gopsutil does not
// expose is read from macOS-native sources: system_profiler (-json), ioreg,
// sysctl, sw_vers and route.
//
// Mirrors internal/collector/{linux,windows,freebsd}. Pure parsers live in
// parse.go (no build tag) so they are unit-tested on any platform.
package macos
