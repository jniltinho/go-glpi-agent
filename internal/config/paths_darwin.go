//go:build darwin

package config

// defaultBaseDir is the install/state root on macOS. The .pkg payload installs
// the binary and agent.cfg here (writable and outside SIP-protected locations),
// with a LaunchDaemon under /Library/LaunchDaemons driving periodic runs.
func defaultBaseDir() string { return "/usr/local/go-glpi-agent" }
