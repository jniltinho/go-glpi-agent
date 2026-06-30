//go:build windows

package cmd

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"

	"github.com/spf13/cobra"
	"golang.org/x/sys/windows/registry"
)

// These commands are the glue the MSI installer calls (via deferred custom
// actions) to register/remove the Scheduled Task, seed the config from the
// SERVER/TAG install properties, and purge state on an opt-in uninstall. They
// are hidden because operators do not run them directly.

const (
	taskName = "go-glpi-agent"
	regKey   = `Software\go-glpi-agent`
)

// defaultConfig is the agent.cfg seeded on first install when none exists. The
// SERVER/TAG passed to msiexec are appended by `service configure`.
const defaultConfig = "# go-glpi-agent configuration (Windows). INI format.\r\n" +
	"#   config : C:\\ProgramData\\go-glpi-agent\\agent.cfg\r\n" +
	"#   state  : C:\\ProgramData\\go-glpi-agent\\var\r\n" +
	"#\r\n" +
	"# Set the GLPI inventory endpoint (or install with: msiexec /i ... SERVER=<url>).\r\n" +
	"# server = http://glpi.example.com/front/inventory.php\r\n" +
	"# tag = windows-fleet\r\n" +
	"# scan-processes = 0\r\n" +
	"# logger = File\r\n" +
	"# logfile = C:\\ProgramData\\go-glpi-agent\\go-glpi-agent.log\r\n" +
	"# debug = 0\r\n"

// dataDir is the per-machine state/config root on Windows (%ProgramData%\go-glpi-agent).
func dataDir() string {
	base := os.Getenv("ProgramData")
	if base == "" {
		base = `C:\ProgramData`
	}
	return filepath.Join(base, "go-glpi-agent")
}

var serviceCmd = &cobra.Command{
	Use:    "service",
	Short:  "Manage the Windows install (used by the MSI installer)",
	Hidden: true,
}

var serviceInstallCmd = &cobra.Command{
	Use:   "install",
	Short: "Seed the config (if absent) and register the hourly Scheduled Task",
	RunE: func(cmd *cobra.Command, args []string) error {
		// Ensure the data dir and a default agent.cfg exist. Writing the config
		// only when absent gives upgrade-safe preservation without relying on MSI
		// component flags (wixl supports neither Permanent nor NeverOverwrite).
		dir := dataDir()
		if err := os.MkdirAll(filepath.Join(dir, "var"), 0o755); err != nil {
			return fmt.Errorf("create %s: %w", dir, err)
		}
		cfgPath := filepath.Join(dir, "agent.cfg")
		if _, err := os.Stat(cfgPath); os.IsNotExist(err) {
			if werr := os.WriteFile(cfgPath, []byte(defaultConfig), 0o644); werr != nil {
				return fmt.Errorf("write %s: %w", cfgPath, werr)
			}
		}

		exe, err := os.Executable()
		if err != nil {
			return err
		}
		out, err := exec.Command("schtasks.exe", "/Create", "/F",
			"/TN", taskName, "/SC", "HOURLY", "/RU", "SYSTEM", "/RL", "HIGHEST",
			"/TR", fmt.Sprintf(`"%s" run`, exe)).CombinedOutput()
		if err != nil {
			return fmt.Errorf("schtasks create: %v: %s", err, strings.TrimSpace(string(out)))
		}
		fmt.Printf("Scheduled Task %q registered for %s\n", taskName, exe)
		return nil
	},
}

var serviceUninstallCmd = &cobra.Command{
	Use:   "uninstall",
	Short: "Remove the Scheduled Task",
	RunE: func(cmd *cobra.Command, args []string) error {
		// Best-effort: the task may already be gone.
		_ = exec.Command("schtasks.exe", "/Delete", "/F", "/TN", taskName).Run()
		fmt.Printf("Scheduled Task %q removed\n", taskName)
		return nil
	},
}

var servicePurgeCmd = &cobra.Command{
	Use:   "purge",
	Short: "Remove the config/state directory",
	RunE: func(cmd *cobra.Command, args []string) error {
		dir := dataDir()
		if err := os.RemoveAll(dir); err != nil {
			return fmt.Errorf("remove %s: %w", dir, err)
		}
		fmt.Printf("Removed %s\n", dir)
		return nil
	},
}

// serviceConfigureCmd seeds agent.cfg from the SERVER/TAG values the MSI wrote to
// HKLM (the installer's public properties). It only appends a line when the value
// is non-empty and not already present, so re-runs and upgrades are idempotent.
var serviceConfigureCmd = &cobra.Command{
	Use:   "configure",
	Short: "Seed agent.cfg from the installer's SERVER/TAG values",
	RunE: func(cmd *cobra.Command, args []string) error {
		server := regString("Server")
		tag := regString("Tag")
		if server == "" && tag == "" {
			return nil
		}
		cfgPath := filepath.Join(dataDir(), "agent.cfg")
		b, _ := os.ReadFile(cfgPath)
		content := string(b)

		var add []string
		if server != "" && !hasActiveKey(content, "server") {
			add = append(add, "server = "+server)
		}
		if tag != "" && !hasActiveKey(content, "tag") {
			add = append(add, "tag = "+tag)
		}
		if len(add) == 0 {
			return nil
		}
		if content != "" && !strings.HasSuffix(content, "\n") {
			content += "\r\n"
		}
		content += strings.Join(add, "\r\n") + "\r\n"
		if err := os.WriteFile(cfgPath, []byte(content), 0o644); err != nil {
			return fmt.Errorf("write %s: %w", cfgPath, err)
		}
		fmt.Printf("Configured %s (%s)\n", cfgPath, strings.Join(add, ", "))
		return nil
	},
}

// regString reads a string value from HKLM\Software\go-glpi-agent, or "".
func regString(name string) string {
	k, err := registry.OpenKey(registry.LOCAL_MACHINE, regKey, registry.QUERY_VALUE)
	if err != nil {
		return ""
	}
	defer k.Close()
	v, _, err := k.GetStringValue(name)
	if err != nil {
		return ""
	}
	return strings.TrimSpace(v)
}

// hasActiveKey reports whether content has an uncommented "<key> =" line.
func hasActiveKey(content, key string) bool {
	for _, line := range strings.Split(content, "\n") {
		line = strings.TrimSpace(line)
		if strings.HasPrefix(line, "#") {
			continue
		}
		if k, _, ok := strings.Cut(line, "="); ok && strings.TrimSpace(k) == key {
			return true
		}
	}
	return false
}

func init() {
	serviceCmd.AddCommand(serviceInstallCmd, serviceUninstallCmd, servicePurgeCmd, serviceConfigureCmd)
	rootCmd.AddCommand(serviceCmd)
}
