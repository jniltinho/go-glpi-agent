//go:build windows

// Package windows holds the Windows-specific inventory collectors. Each
// collector registers itself with the collector registry at init time and runs
// only when the agent is executing on Windows (gated by IsEnabled). Cross-platform
// signals (CPU/mem/disk/net/host/process) come from gopsutil; data gopsutil does
// not expose is read via WMI (github.com/yusufpapurcu/wmi) and the registry
// (golang.org/x/sys/windows/registry).
//
// This package mirrors internal/collector/linux. Adding another OS (macOS, BSD)
// is the same shape: a sibling package gated by its build tag, registered from
// internal/agent/register_<goos>.go.
package windows
