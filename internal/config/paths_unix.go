//go:build !windows && !darwin

package config

// defaultBaseDir is the install/state root on Unix-like platforms (Linux and the
// BSDs). macOS overrides it in paths_darwin.go with a macOS-appropriate prefix.
func defaultBaseDir() string { return "/opt/go-glpi-agent" }
