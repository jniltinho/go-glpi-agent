//go:build linux

package linux

import (
	"encoding/json"
	"testing"
)

// TestLsblkParseFlexTypes ensures lsblk JSON parses whether values are real
// numbers/booleans (newer util-linux) or quoted strings (older util-linux on
// AlmaLinux/Oracle 8, which previously broke the storages collector).
func TestLsblkParseFlexTypes(t *testing.T) {
	cases := map[string]string{
		"newer": `{"blockdevices":[{"name":"sda","type":"disk","size":107374182400,"rota":true}]}`,
		"older": `{"blockdevices":[{"name":"sda","type":"disk","size":"107374182400","rota":"1"}]}`,
	}
	for name, in := range cases {
		var out lsblkOutput
		if err := json.Unmarshal([]byte(in), &out); err != nil {
			t.Fatalf("%s: unmarshal: %v", name, err)
		}
		if len(out.BlockDevices) != 1 {
			t.Fatalf("%s: got %d devices", name, len(out.BlockDevices))
		}
		d := out.BlockDevices[0]
		if int64(d.Size) != 107374182400 {
			t.Errorf("%s: size = %d, expected 107374182400", name, int64(d.Size))
		}
		if !bool(d.Rota) {
			t.Errorf("%s: rota = false, expected true", name)
		}
	}
}
