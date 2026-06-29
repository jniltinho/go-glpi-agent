package cmd

import (
	"context"

	"github.com/spf13/cobra"
)

// daemonCmd runs the agent as a long-running daemon with periodic cycles.
var daemonCmd = &cobra.Command{
	Use:   "daemon",
	Short: "Run the agent as a daemon with periodic inventory cycles",
	RunE: func(cmd *cobra.Command, args []string) error {
		a, log, err := buildAgent(cmd)
		if err != nil {
			return err
		}
		if err := a.RunDaemon(context.Background()); err != nil {
			log.Error("daemon: %v", err)
			return err
		}
		return nil
	},
}
