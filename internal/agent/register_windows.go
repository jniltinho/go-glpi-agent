//go:build windows

package agent

// Registers the Windows collectors via their package init() functions.
import _ "go-glpi-agent/internal/collector/windows"
