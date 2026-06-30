//go:build windows

package config

import (
	"os"
	"path/filepath"
)

// defaultBaseDir is the install/state root on Windows: %ProgramData%\go-glpi-agent
// (e.g. C:\ProgramData\go-glpi-agent), falling back to a fixed path when the
// environment variable is unset.
func defaultBaseDir() string {
	if pd := os.Getenv("ProgramData"); pd != "" {
		return filepath.Join(pd, "go-glpi-agent")
	}
	return `C:\ProgramData\go-glpi-agent`
}
