package cmd

import (
	"context"

	"github.com/spf13/cobra"
)

// runCmd runs a single inventory cycle and exits.
var runCmd = &cobra.Command{
	Use:   "run",
	Short: "Run a single inventory cycle and exit",
	RunE: func(cmd *cobra.Command, args []string) error {
		a, log, err := buildAgent(cmd)
		if err != nil {
			return err
		}
		if err := a.RunOnce(context.Background()); err != nil {
			log.Error("inventory: %v", err)
			return err
		}
		return nil
	},
}
