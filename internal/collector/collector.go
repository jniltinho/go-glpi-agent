// Package collector define a interface de coleta de inventário e o motor
// que executa os coletores registrados de forma concorrente.
package collector

import (
	"context"
	"sync"
	"time"

	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/logger"
)

// Collector é a unidade de coleta. Cada tipo de dado (CPU, rede, software...)
// implementa esta interface. Espelha o padrão isEnabled/doInventory do Perl.
type Collector interface {
	// Name é o identificador do coletor (usado em logs).
	Name() string
	// Category é a categoria para fins de --no-category (ex: "cpu", "software").
	Category() string
	// IsEnabled decide se o coletor deve rodar neste host/config.
	IsEnabled(cfg *config.Config) bool
	// Collect coleta os dados e os adiciona ao inventário.
	Collect(ctx context.Context, inv *inventory.Inventory) error
}

// registry guarda os coletores registrados via Register.
var registry []Collector

// Register adiciona um coletor ao registry global. Chamado em init() de cada
// pacote de coletores.
func Register(c Collector) {
	registry = append(registry, c)
}

// Registered retorna a lista de coletores registrados.
func Registered() []Collector {
	return registry
}

// Engine executa os coletores habilitados concorrentemente.
type Engine struct {
	cfg     *config.Config
	log     *logger.Logger
	timeout time.Duration
}

// NewEngine cria o motor com a configuração e logger fornecidos.
func NewEngine(cfg *config.Config, log *logger.Logger) *Engine {
	timeout := time.Duration(cfg.BackendCollectTimeout) * time.Second
	if timeout <= 0 {
		timeout = 180 * time.Second
	}
	return &Engine{cfg: cfg, log: log, timeout: timeout}
}

// Run executa todos os coletores habilitados em paralelo. Cada coletor tem um
// timeout individual; um que exceda o timeout ou retorne erro é logado como
// warning sem cancelar os demais.
func (e *Engine) Run(ctx context.Context, inv *inventory.Inventory) {
	var wg sync.WaitGroup

	for _, c := range Registered() {
		c := c
		if !c.IsEnabled(e.cfg) {
			e.log.Debug("coletor %s desabilitado", c.Name())
			continue
		}
		if e.cfg.CategoryDisabled(c.Category()) {
			e.log.Debug("coletor %s pulado (no-category=%s)", c.Name(), c.Category())
			continue
		}

		wg.Add(1)
		go func() {
			defer wg.Done()
			cctx, cancel := context.WithTimeout(ctx, e.timeout)
			defer cancel()

			done := make(chan error, 1)
			go func() { done <- c.Collect(cctx, inv) }()

			select {
			case <-cctx.Done():
				e.log.Warning("coletor %s excedeu timeout (%s)", c.Name(), e.timeout)
			case err := <-done:
				if err != nil {
					e.log.Warning("coletor %s falhou: %v", c.Name(), err)
				} else {
					e.log.Debug("coletor %s concluído", c.Name())
				}
			}
		}()
	}

	wg.Wait()
}
