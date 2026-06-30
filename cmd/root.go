// Package cmd provides the command-line interface for fusioninventory-agent,
// built with Cobra. Global flags live on the root command and are shared by the
// run and daemon subcommands.
package cmd

import (
	"fmt"
	"os"
	"strings"

	"github.com/spf13/cobra"

	"go-glpi-agent/internal/agent"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/logger"
)

// Global flags shared across subcommands.
var (
	flagConfFile   string
	flagServer     string
	flagLocal      string
	flagNoCategory string
	flagDebug      bool
	flagForce      bool
)

// rootCmd is the base command. It only holds shared flags; the actual work is
// done by the run and daemon subcommands.
var rootCmd = &cobra.Command{
	Use:   "go-glpi-agent",
	Short: "Go inventory agent for GLPI (native JSON) and legacy OCS/FusionInventory (XML)",
	Long: `go-glpi-agent collects local hardware and software inventory on Linux and
sends it to a GLPI 10+ server (native JSON protocol) or writes it to a local XML
file. It reads the same agent.cfg as the Perl FusionInventory/GLPI agent.`,
}

// Execute runs the CLI. It is called from main.
func Execute() {
	if err := rootCmd.Execute(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func init() {
	pf := rootCmd.PersistentFlags()
	pf.StringVar(&flagConfFile, "conf-file", config.DefaultConfFile, "path to agent.cfg")
	pf.StringVar(&flagServer, "server", "", "GLPI server URL")
	pf.StringVar(&flagLocal, "local", "", "directory to write the inventory as XML")
	pf.StringVar(&flagNoCategory, "no-category", "", "categories to disable (comma-separated)")
	pf.BoolVar(&flagDebug, "debug", false, "enable debug logging")
	pf.BoolVar(&flagForce, "force", false, "send inventory even if the server did not request it")

	rootCmd.AddCommand(runCmd)
	rootCmd.AddCommand(daemonCmd)
	rootCmd.AddCommand(versionCmd)
}

// buildAgent loads the configuration, applies flag overrides, builds the logger
// and constructs the agent. Shared by the run and daemon subcommands.
func buildAgent(cmd *cobra.Command) (*agent.Agent, *logger.Logger, error) {
	cfg := config.Default()
	if _, err := os.Stat(flagConfFile); err == nil {
		loaded, lerr := config.Load(flagConfFile)
		if lerr != nil {
			return nil, nil, fmt.Errorf("reading %s: %w", flagConfFile, lerr)
		}
		cfg = loaded
	} else if cmd.Flags().Changed("conf-file") {
		return nil, nil, fmt.Errorf("configuration file not found: %s", flagConfFile)
	}

	// Command-line flags override the configuration file.
	flags := cmd.Flags()
	if flags.Changed("server") {
		cfg.Server = flagServer
	}
	if flags.Changed("local") {
		cfg.Local = flagLocal
	}
	if flags.Changed("debug") {
		cfg.Debug = flagDebug
	}
	if flags.Changed("force") {
		cfg.Force = flagForce
	}
	if flags.Changed("no-category") {
		for _, c := range strings.Split(flagNoCategory, ",") {
			if c = strings.TrimSpace(c); c != "" {
				cfg.NoCategory = append(cfg.NoCategory, c)
			}
		}
	}

	log := logger.New(logger.Options{
		Backend:     cfg.Logger,
		LogFile:     cfg.LogFile,
		LogFacility: cfg.LogFacility,
		Debug:       cfg.Debug,
	})
	for _, k := range cfg.UnknownKeys {
		log.Debug("ignoring unknown configuration key: %s", k)
	}

	a, err := agent.New(cfg, log)
	if err != nil {
		return nil, log, err
	}
	return a, log, nil
}
