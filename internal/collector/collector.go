// Package collector defines the inventory collection interface and the engine
// that runs the registered collectors concurrently.
package collector

import (
	"context"
	"sync"
	"time"

	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/logger"
)

// Collector is the unit of collection. Each kind of data (CPU, network, software...)
// implements this interface. It mirrors the Perl isEnabled/doInventory pattern.
type Collector interface {
	// Name is the collector identifier (used in logs).
	Name() string
	// Category is the category for --no-category purposes (e.g. "cpu", "software").
	Category() string
	// IsEnabled decides whether the collector should run on this host/config.
	IsEnabled(cfg *config.Config) bool
	// Collect gathers the data and adds it to the inventory.
	Collect(ctx context.Context, inv *inventory.Inventory) error
}

// registry holds the collectors registered via Register.
var registry []Collector

// Register adds a collector to the global registry. Called from each collector
// package's init().
func Register(c Collector) {
	registry = append(registry, c)
}

// Registered returns the list of registered collectors.
func Registered() []Collector {
	return registry
}

// Engine runs the enabled collectors concurrently.
type Engine struct {
	cfg     *config.Config
	log     *logger.Logger
	timeout time.Duration
}

// NewEngine creates the engine with the given config and logger.
func NewEngine(cfg *config.Config, log *logger.Logger) *Engine {
	timeout := time.Duration(cfg.BackendCollectTimeout) * time.Second
	if timeout <= 0 {
		timeout = 180 * time.Second
	}
	return &Engine{cfg: cfg, log: log, timeout: timeout}
}

// Run executes all enabled collectors in parallel. Each collector has its own
// timeout; one that exceeds the timeout or returns an error is logged as a
// warning without canceling the others.
func (e *Engine) Run(ctx context.Context, inv *inventory.Inventory) {
	var wg sync.WaitGroup

	for _, c := range Registered() {
		c := c
		if !c.IsEnabled(e.cfg) {
			e.log.Debug("collector %s disabled", c.Name())
			continue
		}
		if e.cfg.CategoryDisabled(c.Category()) {
			e.log.Debug("collector %s skipped (no-category=%s)", c.Name(), c.Category())
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
				e.log.Warning("collector %s exceeded timeout (%s)", c.Name(), e.timeout)
			case err := <-done:
				if err != nil {
					e.log.Warning("collector %s failed: %v", c.Name(), err)
				} else {
					e.log.Debug("collector %s completed", c.Name())
				}
			}
		}()
	}

	wg.Wait()
}
