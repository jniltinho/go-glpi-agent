// Package transport defines the inventory target interface (GLPI server
// or local file).
package transport

import (
	"context"

	"go-fusioninventory-agent/internal/inventory"
)

// Target is a destination the inventory is sent/written to.
type Target interface {
	// Send delivers the inventory to the target.
	Send(ctx context.Context, inv *inventory.Inventory) error
}
