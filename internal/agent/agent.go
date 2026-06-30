// Package agent orchestrates the inventory run: it resolves the device ID,
// runs the collectors and delivers the result to the target (server or local).
package agent

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"syscall"
	"time"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/logger"
	"go-glpi-agent/internal/transport"
	"go-glpi-agent/internal/transport/local"
	"go-glpi-agent/internal/transport/server"

	// Cross-platform collectors register here; OS-specific collectors are
	// blank-imported from the per-OS register_<goos>.go files.
	_ "go-glpi-agent/internal/collector/generic"
)

// Agent brings together configuration, logger and the inventory target.
type Agent struct {
	cfg    *config.Config
	log    *logger.Logger
	target transport.Target
}

// New creates an agent with the appropriate target (server takes priority over
// local). Returns an error if no target was configured.
func New(cfg *config.Config, log *logger.Logger) (*Agent, error) {
	a := &Agent{cfg: cfg, log: log}

	switch {
	case cfg.Server != "":
		t, err := server.New(cfg, log)
		if err != nil {
			return nil, err
		}
		a.target = t
	case cfg.Local != "":
		a.target = local.New(cfg.Local, log)
	default:
		return nil, fmt.Errorf("no target configured (use --server or --local)")
	}
	return a, nil
}

// RunOnce executes a single inventory cycle.
func (a *Agent) RunOnce(ctx context.Context) error {
	hostname, _ := os.Hostname()
	deviceID, err := LoadOrCreateDeviceID(a.cfg.VarDir, hostname, time.Now())
	if err != nil {
		a.log.Warning("device id: %v", err)
	}
	a.log.Info("device id: %s", deviceID)

	agentID, err := LoadOrCreateAgentID(a.cfg.VarDir)
	if err != nil {
		a.log.Warning("agent id: %v", err)
	}
	a.log.Debug("agent id: %s", agentID)

	inv := inventory.New(deviceID)
	inv.AgentID = agentID
	inv.Tag = a.cfg.Tag

	engine := collector.NewEngine(a.cfg, a.log)
	a.log.Info("collecting inventory...")
	engine.Run(ctx, inv)

	a.log.Info("sending inventory...")
	return a.target.Send(ctx, inv)
}

// RunDaemon executes periodic cycles until it receives SIGTERM/SIGINT.
func (a *Agent) RunDaemon(ctx context.Context) error {
	ctx, stop := signal.NotifyContext(ctx, syscall.SIGTERM, syscall.SIGINT)
	defer stop()

	delay := time.Duration(a.cfg.DelayTime) * time.Second
	if delay <= 0 {
		delay = time.Hour
	}

	a.log.Info("daemon mode started (interval: %s)", delay)
	for {
		if err := a.RunOnce(ctx); err != nil {
			a.log.Error("inventory cycle failed: %v", err)
		}

		a.log.Debug("waiting %s until the next cycle", delay)
		select {
		case <-ctx.Done():
			a.log.Info("signal received, shutting down daemon")
			return nil
		case <-time.After(delay):
		}
	}
}
