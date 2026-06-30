// Command go-glpi-agent is a Go inventory agent for Linux, compatible with the
// GLPI native (JSON) protocol and the legacy OCS/FusionInventory XML protocol.
package main

import "go-glpi-agent/cmd"

// main is the program entry point; it delegates to the Cobra command tree.
func main() {
	cmd.Execute()
}
