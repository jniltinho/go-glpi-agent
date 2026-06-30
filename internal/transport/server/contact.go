package server

import (
	"bytes"
	"context"
	"encoding/json"
	"os"

	"go-glpi-agent/internal/inventory"
)

// contactReply is the (loosely typed) JSON answer to a native CONTACT request.
// GLPI 10+ returns a JSON object describing the agent's tasks and scheduling;
// only the fields used to decide whether to send an inventory are modeled here.
type contactReply struct {
	Status   string          `json:"status"`
	Disabled json.RawMessage `json:"disabled,omitempty"`
	Tasks    json.RawMessage `json:"tasks,omitempty"`
}

// contact probes the server with a native CONTACT request. It returns:
//   - native: whether the server answered with the native JSON protocol;
//   - wantInventory: whether the server expects an inventory this cycle;
//   - err: a transport error (the caller falls back to legacy on error).
//
// Detection is based on the reply being valid JSON: the GLPI native endpoint
// answers CONTACT with JSON, whereas a legacy OCS/FusionInventory plugin does
// not understand the request.
func (t *Target) contact(ctx context.Context, inv *inventory.Inventory) (native, wantInventory bool, err error) {
	name, _ := os.Hostname()
	body, err := BuildContactJSON(inv.DeviceID, name, inv.Tag)
	if err != nil {
		return false, false, err
	}

	resp, err := t.postJSON(ctx, body)
	if err != nil {
		return false, false, err
	}

	resp = bytes.TrimSpace(resp)
	if len(resp) == 0 || resp[0] != '{' {
		// Not a JSON reply: treat as a legacy server.
		return false, false, nil
	}

	var reply contactReply
	if jerr := json.Unmarshal(resp, &reply); jerr != nil {
		return false, false, nil
	}

	// Native server confirmed. v1 always sends the inventory; precise lazy
	// scheduling from the reply is a follow-up (tasks.md 4.6).
	t.log.Debug("native CONTACT accepted (status=%q)", reply.Status)
	return true, true, nil
}
