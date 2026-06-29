// Package local implementa o transporte que grava o inventário em arquivo XML.
package local

import (
	"context"
	"fmt"
	"os"
	"path/filepath"

	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/logger"
	"go-fusioninventory-agent/internal/transport/server"
)

// Target grava o inventário como <DEVICEID>.xml num diretório.
type Target struct {
	dir string
	log *logger.Logger
}

// New cria um Target local apontando para o diretório dir.
func New(dir string, log *logger.Logger) *Target {
	return &Target{dir: dir, log: log}
}

// Send serializa o inventário e grava em {dir}/{DEVICEID}.xml.
func (t *Target) Send(ctx context.Context, inv *inventory.Inventory) error {
	if err := os.MkdirAll(t.dir, 0o755); err != nil {
		return fmt.Errorf("criar diretório %s: %w", t.dir, err)
	}
	body, err := server.Serialize(inv)
	if err != nil {
		return err
	}
	path := filepath.Join(t.dir, inv.DeviceID+".xml")
	if err := os.WriteFile(path, body, 0o644); err != nil {
		return fmt.Errorf("gravar %s: %w", path, err)
	}
	t.log.Info("inventário gravado em %s (%d bytes)", path, len(body))
	return nil
}
