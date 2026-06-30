//go:build darwin

package agent

// Registers the macOS collectors via their package init() functions. Adding a
// new OS is just a sibling register_<goos>.go with the matching build tag.
import _ "go-glpi-agent/internal/collector/macos"
