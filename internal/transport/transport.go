// Package transport define a interface de destino do inventário (servidor
// GLPI ou arquivo local).
package transport

import (
	"context"

	"go-fusioninventory-agent/internal/inventory"
)

// Target é um destino para onde o inventário é enviado/gravado.
type Target interface {
	// Send entrega o inventário ao destino.
	Send(ctx context.Context, inv *inventory.Inventory) error
}
