//go:build windows

package config

import (
	"os"
	"path/filepath"
)

// defaultBaseDir is the state root on Windows: %ProgramData%\go-glpi-agent
// (e.g. C:\ProgramData\go-glpi-agent), falling back to a fixed path when the
// environment variable is unset. Holds var/ (state) and the log file.
func defaultBaseDir() string {
	if pd := os.Getenv("ProgramData"); pd != "" {
		return filepath.Join(pd, "go-glpi-agent")
	}
	return `C:\ProgramData\go-glpi-agent`
}

// defaultConfFile lives next to the binary on Windows (the MSI installs to
// %ProgramFiles%\go-glpi-agent and `service install` seeds agent.cfg there),
// so a portable copy of the exe + agent.cfg works from any directory.
func defaultConfFile() string {
	if exe, err := os.Executable(); err == nil {
		return filepath.Join(filepath.Dir(exe), "agent.cfg")
	}
	return filepath.Join(defaultBaseDir(), "agent.cfg")
}
