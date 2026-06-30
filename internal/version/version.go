// Package version centralizes the agent version and the User-Agent string
// used when communicating with GLPI.
package version

// Version is the Go agent version. Overridden at build time via -ldflags.
var Version = "0.1.0-dev"

// Name is the agent name (distinct from the Perl fusioninventory-agent).
const Name = "go-glpi-agent"

// UserAgent returns the string sent as VERSIONCLIENT in the XML and as the
// User-Agent header in HTTP requests. GLPI expects the prefix
// "FusionInventory-Agent" to recognize the client.
func UserAgent() string {
	return "FusionInventory-Agent_v" + Version + " (" + Name + ")"
}

// GLPIUserAgent returns the native protocol User-Agent. GLPI 10+ recognizes
// the modern client by the prefix "GLPI-Agent_v".
func GLPIUserAgent() string {
	return "GLPI-Agent_v" + Version
}
