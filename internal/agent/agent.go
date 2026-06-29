// Package agent orquestra a execução do inventário: resolve o device ID,
// roda os coletores e entrega o resultado ao destino (server ou local).
package agent

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"syscall"
	"time"

	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/logger"
	"go-fusioninventory-agent/internal/transport"
	"go-fusioninventory-agent/internal/transport/local"
	"go-fusioninventory-agent/internal/transport/server"

	// importa os coletores para registrar via init()
	_ "go-fusioninventory-agent/internal/collector/generic"
	_ "go-fusioninventory-agent/internal/collector/linux"
)

// Agent reúne configuração, logger e o destino do inventário.
type Agent struct {
	cfg    *config.Config
	log    *logger.Logger
	target transport.Target
}

// New cria um agente com o destino apropriado (server tem prioridade sobre
// local). Retorna erro se nenhum destino foi configurado.
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
		return nil, fmt.Errorf("nenhum destino configurado (use --server ou --local)")
	}
	return a, nil
}

// RunOnce executa um único ciclo de inventário.
func (a *Agent) RunOnce(ctx context.Context) error {
	hostname, _ := os.Hostname()
	deviceID, err := LoadOrCreateDeviceID(a.cfg.VarDir, hostname, time.Now())
	if err != nil {
		a.log.Warning("device id: %v", err)
	}
	a.log.Info("device id: %s", deviceID)

	inv := inventory.New(deviceID)
	inv.Tag = a.cfg.Tag

	engine := collector.NewEngine(a.cfg, a.log)
	a.log.Info("coletando inventário...")
	engine.Run(ctx, inv)

	a.log.Info("enviando inventário...")
	return a.target.Send(ctx, inv)
}

// RunDaemon executa ciclos periódicos até receber SIGTERM/SIGINT.
func (a *Agent) RunDaemon(ctx context.Context) error {
	ctx, stop := signal.NotifyContext(ctx, syscall.SIGTERM, syscall.SIGINT)
	defer stop()

	delay := time.Duration(a.cfg.DelayTime) * time.Second
	if delay <= 0 {
		delay = time.Hour
	}

	a.log.Info("modo daemon iniciado (intervalo: %s)", delay)
	for {
		if err := a.RunOnce(ctx); err != nil {
			a.log.Error("ciclo de inventário falhou: %v", err)
		}

		a.log.Debug("aguardando %s até o próximo ciclo", delay)
		select {
		case <-ctx.Done():
			a.log.Info("sinal recebido, encerrando daemon")
			return nil
		case <-time.After(delay):
		}
	}
}
