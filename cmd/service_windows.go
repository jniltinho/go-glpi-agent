//go:build windows

package cmd

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"

	"github.com/spf13/cobra"
	"golang.org/x/sys/windows/registry"
	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/mgr"
)

// These commands are the glue the MSI installer calls (via deferred custom
// actions) to register/remove the Windows Service, seed the config from the
// SERVER/TAG install properties, and purge state on an opt-in uninstall. They
// are hidden because operators do not run them directly.

const (
	serviceName    = "go-glpi-agent"
	serviceDisplay = "Go GLPI Agent"
	serviceDesc    = "Collects hardware and software inventory and sends it to GLPI."
	legacyTaskName = "go-glpi-agent" // Scheduled Task used by releases <= 0.5.x
	regKey         = `Software\go-glpi-agent`
)

// defaultConfig is the agent.cfg seeded next to the binary on first install
// when none exists. The SERVER/TAG passed to msiexec are appended by
// `service configure`. logger/logfile are active because the service has no
// console to write to.
const defaultConfig = "# go-glpi-agent configuration (Windows). INI format.\r\n" +
	"#   config : agent.cfg next to go-glpi-agent.exe\r\n" +
	"#   state  : C:\\ProgramData\\go-glpi-agent\\var\r\n" +
	"#\r\n" +
	"# Set the GLPI inventory endpoint (or install with: msiexec /i ... SERVER=<url>).\r\n" +
	"# server = http://glpi.example.com/front/inventory.php\r\n" +
	"# tag = windows-fleet\r\n" +
	"# scan-processes = 0\r\n" +
	"# debug = 0\r\n" +
	"logger = File\r\n" +
	"logfile = C:\\ProgramData\\go-glpi-agent\\go-glpi-agent.log\r\n"

// dataDir is the per-machine state root on Windows (%ProgramData%\go-glpi-agent).
func dataDir() string {
	base := os.Getenv("ProgramData")
	if base == "" {
		base = `C:\ProgramData`
	}
	return filepath.Join(base, "go-glpi-agent")
}

// configPath is the agent.cfg next to the binary (same folder as the exe).
func configPath() (string, error) {
	exe, err := os.Executable()
	if err != nil {
		return "", err
	}
	return filepath.Join(filepath.Dir(exe), "agent.cfg"), nil
}

var serviceCmd = &cobra.Command{
	Use:    "service",
	Short:  "Manage the Windows install (used by the MSI installer)",
	Hidden: true,
}

var serviceInstallCmd = &cobra.Command{
	Use:   "install",
	Short: "Seed the config (if absent) and register the Windows Service",
	RunE: func(cmd *cobra.Command, args []string) error {
		// Ensure the state dir and a default agent.cfg (next to the exe) exist.
		// Writing the config only when absent gives upgrade-safe preservation
		// without relying on MSI component flags (wixl supports neither
		// Permanent nor NeverOverwrite).
		if err := os.MkdirAll(filepath.Join(dataDir(), "var"), 0o755); err != nil {
			return fmt.Errorf("create %s: %w", dataDir(), err)
		}
		cfgPath, err := configPath()
		if err != nil {
			return err
		}
		if _, err := os.Stat(cfgPath); os.IsNotExist(err) {
			if werr := os.WriteFile(cfgPath, []byte(defaultConfig), 0o644); werr != nil {
				return fmt.Errorf("write %s: %w", cfgPath, werr)
			}
		}

		// Releases <= 0.5.x registered a Scheduled Task instead of a service.
		_ = exec.Command("schtasks.exe", "/Delete", "/F", "/TN", legacyTaskName).Run()

		exe, err := os.Executable()
		if err != nil {
			return err
		}
		if err := installService(exe); err != nil {
			return err
		}
		fmt.Printf("Windows Service %q registered for %s\n", serviceName, exe)
		return nil
	},
}

// installService (re)creates and starts the SCM service entry pointing at
// `<exe> service run`. An existing entry is removed first so upgrades always
// refresh the binary path.
func installService(exe string) error {
	m, err := mgr.Connect()
	if err != nil {
		return fmt.Errorf("connect to service manager: %w", err)
	}
	defer m.Disconnect()

	if s, err := m.OpenService(serviceName); err == nil {
		stopService(s)
		err = s.Delete()
		s.Close()
		if err != nil {
			return fmt.Errorf("remove previous service: %w", err)
		}
		waitServiceGone(m)
	}

	s, err := m.CreateService(serviceName, exe, mgr.Config{
		DisplayName:      serviceDisplay,
		Description:      serviceDesc,
		StartType:        mgr.StartAutomatic,
		DelayedAutoStart: true,
	}, "service", "run")
	if err != nil {
		return fmt.Errorf("create service: %w", err)
	}
	defer s.Close()

	// Parity with the Dotnet agent: restart twice after 30s, then give up.
	_ = s.SetRecoveryActions([]mgr.RecoveryAction{
		{Type: mgr.ServiceRestart, Delay: 30 * time.Second},
		{Type: mgr.ServiceRestart, Delay: 30 * time.Second},
		{Type: mgr.NoAction},
	}, 86400)

	if err := s.Start(); err != nil {
		// Not fatal: the service is registered and will start on next boot.
		fmt.Printf("warning: service start: %v\n", err)
	}
	return nil
}

// stopService asks a service to stop and waits (up to 30s) for it to reach
// the Stopped state. Best effort: errors are ignored by the callers.
func stopService(s *mgr.Service) {
	status, err := s.Control(svc.Stop)
	if err != nil {
		return
	}
	for range 30 {
		if status.State == svc.Stopped {
			return
		}
		time.Sleep(time.Second)
		if status, err = s.Query(); err != nil {
			return
		}
	}
}

// waitServiceGone waits for a deleted service to disappear from the SCM (the
// deletion is asynchronous once every handle is closed).
func waitServiceGone(m *mgr.Mgr) {
	for range 10 {
		s, err := m.OpenService(serviceName)
		if err != nil {
			return
		}
		s.Close()
		time.Sleep(time.Second)
	}
}

var serviceUninstallCmd = &cobra.Command{
	Use:   "uninstall",
	Short: "Stop and remove the Windows Service",
	RunE: func(cmd *cobra.Command, args []string) error {
		// Best-effort: the service (or the legacy Scheduled Task) may be gone.
		_ = exec.Command("schtasks.exe", "/Delete", "/F", "/TN", legacyTaskName).Run()
		m, err := mgr.Connect()
		if err != nil {
			return fmt.Errorf("connect to service manager: %w", err)
		}
		defer m.Disconnect()
		if s, err := m.OpenService(serviceName); err == nil {
			stopService(s)
			_ = s.Delete()
			s.Close()
		}
		fmt.Printf("Windows Service %q removed\n", serviceName)
		return nil
	},
}

var servicePurgeCmd = &cobra.Command{
	Use:   "purge",
	Short: "Remove the config and the state directory",
	RunE: func(cmd *cobra.Command, args []string) error {
		if err := os.RemoveAll(dataDir()); err != nil {
			return fmt.Errorf("remove %s: %w", dataDir(), err)
		}
		if cfgPath, err := configPath(); err == nil {
			_ = os.Remove(cfgPath)
		}
		fmt.Printf("Removed %s and agent.cfg\n", dataDir())
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
		cfgPath, err := configPath()
		if err != nil {
			return err
		}
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

		// The service may have started before SERVER/TAG landed in agent.cfg;
		// restart it so the daemon picks up the new target. Best effort.
		restartService()
		return nil
	},
}

// restartService bounces the Windows Service so configuration changes apply.
func restartService() {
	m, err := mgr.Connect()
	if err != nil {
		return
	}
	defer m.Disconnect()
	s, err := m.OpenService(serviceName)
	if err != nil {
		return
	}
	defer s.Close()
	stopService(s)
	_ = s.Start()
}

// serviceRunCmd is the SCM entry point (binPath is `<exe> service run`). When
// started by the SCM it speaks the service protocol; run interactively it just
// behaves like `daemon` (useful for debugging).
var serviceRunCmd = &cobra.Command{
	Use:   "run",
	Short: "Run the agent under the Windows Service Control Manager",
	RunE: func(cmd *cobra.Command, args []string) error {
		isService, err := svc.IsWindowsService()
		if err != nil {
			return fmt.Errorf("detect service context: %w", err)
		}
		a, log, err := buildAgent(cmd)
		if err != nil {
			return err
		}
		if !isService {
			return a.RunDaemon(context.Background())
		}
		if err := svc.Run(serviceName, &agentService{
			run: a.RunDaemon,
		}); err != nil {
			log.Error("service: %v", err)
			return err
		}
		return nil
	},
}

// agentService adapts the daemon loop to the SCM handshake: it reports
// Running while RunDaemon executes and cancels its context on Stop/Shutdown.
type agentService struct {
	run func(context.Context) error
}

func (h *agentService) Execute(args []string, requests <-chan svc.ChangeRequest, status chan<- svc.Status) (svcSpecificEC bool, exitCode uint32) {
	status <- svc.Status{State: svc.StartPending}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	done := make(chan error, 1)
	go func() { done <- h.run(ctx) }()
	status <- svc.Status{State: svc.Running, Accepts: svc.AcceptStop | svc.AcceptShutdown}

	for {
		select {
		case err := <-done:
			status <- svc.Status{State: svc.StopPending}
			if err != nil {
				return true, 1
			}
			return false, 0
		case c := <-requests:
			switch c.Cmd {
			case svc.Interrogate:
				status <- c.CurrentStatus
			case svc.Stop, svc.Shutdown:
				status <- svc.Status{State: svc.StopPending}
				cancel()
				<-done
				return false, 0
			}
		}
	}
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
	serviceCmd.AddCommand(serviceInstallCmd, serviceUninstallCmd, servicePurgeCmd, serviceConfigureCmd, serviceRunCmd)
	rootCmd.AddCommand(serviceCmd)
}
