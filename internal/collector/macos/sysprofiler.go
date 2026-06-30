//go:build darwin

package macos

import (
	"context"

	"go-glpi-agent/internal/sysutil"
)

// systemProfilerJSON runs `system_profiler -json <dataType>` and returns its raw
// JSON output, or nil on error. The typed decoding lives in parse.go so it stays
// unit-testable off-darwin.
func systemProfilerJSON(ctx context.Context, dataType string) []byte {
	out, err := sysutil.RunContext(ctx, "system_profiler", "-json", "-detailLevel", "mini", dataType)
	if err != nil {
		return nil
	}
	return []byte(out)
}

// systemProfilerHardware returns the parsed SPHardwareDataType overview.
func systemProfilerHardware(ctx context.Context) spHardware {
	return parseSPHardware(systemProfilerJSON(ctx, "SPHardwareDataType"))
}

// ioregPlatform returns the IOPlatformExpertDevice identity (serial/UUID/
// manufacturer/model) as a fallback when system_profiler omits a field.
func ioregPlatform(ctx context.Context) ioPlatform {
	out, err := sysutil.RunContext(ctx, "ioreg", "-d2", "-c", "IOPlatformExpertDevice")
	if err != nil {
		return ioPlatform{}
	}
	return parseIOReg(out)
}
