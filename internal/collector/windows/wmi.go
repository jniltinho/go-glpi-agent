//go:build windows

package windows

import "github.com/yusufpapurcu/wmi"

// queryWMI runs a WQL query and decodes the rows into dst (a pointer to a slice
// of structs whose exported fields match the selected WMI properties). COM is
// initialized per call by the wmi package, so this is safe to call from the
// per-collector goroutines spawned by the engine. WMI itself has no context
// cancellation; the engine's per-collector timeout bounds a slow query.
func queryWMI(query string, dst any) error {
	return wmi.Query(query, dst)
}
