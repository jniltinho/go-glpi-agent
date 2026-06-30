//go:build !windows

package config

// defaultBaseDir is the install/state root on Unix-like platforms. macOS and
// the BSDs can override this with their own paths_<goos>.go (changing this file's
// build constraint to //go:build linux) when they are added.
func defaultBaseDir() string { return "/opt/go-glpi-agent" }
