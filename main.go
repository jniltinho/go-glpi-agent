// Command fusioninventory-agent is a Go inventory agent for Linux, compatible
// with the GLPI native (JSON) protocol and the legacy OCS/FusionInventory XML
// protocol.
package main

import "go-fusioninventory-agent/cmd"

func main() {
	cmd.Execute()
}
