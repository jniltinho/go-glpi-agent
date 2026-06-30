//go:build freebsd

package agent

// Registers the FreeBSD collectors via their package init() functions.
import _ "go-glpi-agent/internal/collector/freebsd"
