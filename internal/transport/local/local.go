// Package local implements the transport that writes the inventory to an XML file.
package local

import (
	"context"
	"fmt"
	"os"
	"path/filepath"

	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/logger"
	"go-glpi-agent/internal/transport/server"
)

// Target writes the inventory as <DEVICEID>.xml in a directory.
type Target struct {
	dir string
	log *logger.Logger
}

// New creates a local Target pointing to the directory dir.
func New(dir string, log *logger.Logger) *Target {
	return &Target{dir: dir, log: log}
}

// Send serializes the inventory and writes it to {dir}/{DEVICEID}.xml.
func (t *Target) Send(ctx context.Context, inv *inventory.Inventory) error {
	if err := os.MkdirAll(t.dir, 0o755); err != nil {
		return fmt.Errorf("create directory %s: %w", t.dir, err)
	}
	body, err := server.Serialize(inv)
	if err != nil {
		return err
	}
	path := filepath.Join(t.dir, inv.DeviceID+".xml")
	if err := os.WriteFile(path, body, 0o644); err != nil {
		return fmt.Errorf("write %s: %w", path, err)
	}
	t.log.Info("inventory written to %s (%d bytes)", path, len(body))

	// Debug aid (same env as the server path): GFI_DUMP_JSON=<file> also writes
	// the native GLPI JSON, so it can be validated against inventory.schema.json
	// offline — handy for CI on a real Windows host without a GLPI server.
	if jsonPath := os.Getenv("GFI_DUMP_JSON"); jsonPath != "" {
		if jb, jerr := server.BuildInventoryJSON(inv); jerr == nil {
			if werr := os.WriteFile(jsonPath, jb, 0o644); werr != nil {
				t.log.Warning("GFI_DUMP_JSON: %v", werr)
			} else {
				t.log.Info("native JSON written to %s (%d bytes)", jsonPath, len(jb))
			}
		} else {
			t.log.Warning("GFI_DUMP_JSON serialize: %v", jerr)
		}
	}
	return nil
}
