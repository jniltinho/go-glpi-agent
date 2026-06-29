package cmd

import (
	"fmt"

	"github.com/spf13/cobra"

	"go-fusioninventory-agent/internal/version"
)

// versionCmd prints the agent version and exits.
var versionCmd = &cobra.Command{
	Use:   "version",
	Short: "Print the agent version",
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Printf("%s %s\n", version.Name, version.Version)
	},
}
