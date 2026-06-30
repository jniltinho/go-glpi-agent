//go:build linux

package linux

import "testing"

func TestCleanDMI(t *testing.T) {
	junk := []string{"", "0", "00000000", "None", "n/a", "Not Specified",
		"To be filled by O.E.M.", "System manufacturer", "Default string", "  0  "}
	for _, s := range junk {
		if got := cleanDMI(s); got != "" {
			t.Errorf("cleanDMI(%q) = %q, expected \"\" (junk)", s, got)
		}
	}
	real := map[string]string{
		"VirtualBox-db2bbc30": "VirtualBox-db2bbc30",
		"  Dell Inc.  ":       "Dell Inc.",
		"CZC1234ABC":          "CZC1234ABC",
	}
	for in, want := range real {
		if got := cleanDMI(in); got != want {
			t.Errorf("cleanDMI(%q) = %q, expected %q", in, got, want)
		}
	}
}
