package sysutil

import "testing"

func TestCleanDMI(t *testing.T) {
	junk := []string{"", "0", "00000000", "None", "n/a", "Not Specified",
		"To be filled by O.E.M.", "System manufacturer", "Default string", "  0  "}
	for _, s := range junk {
		if got := CleanDMI(s); got != "" {
			t.Errorf("CleanDMI(%q) = %q, expected \"\" (junk)", s, got)
		}
	}
	real := map[string]string{
		"VirtualBox-db2bbc30": "VirtualBox-db2bbc30",
		"  Dell Inc.  ":       "Dell Inc.",
		"CZC1234ABC":          "CZC1234ABC",
	}
	for in, want := range real {
		if got := CleanDMI(in); got != want {
			t.Errorf("CleanDMI(%q) = %q, expected %q", in, got, want)
		}
	}
}

func TestVirtualBoxSerial(t *testing.T) {
	uuid := "9E36A1AE-C31D-C94C-8DCD-6F53DF2D2A16"
	// VirtualBox with empty serials -> lowercased uuid (matches glpi-agent)
	if got := VirtualBoxSerial("", "", "VirtualBox", uuid); got != "9e36a1ae-c31d-c94c-8dcd-6f53df2d2a16" {
		t.Errorf("VirtualBox fallback = %q", got)
	}
	// a real serial present -> no fallback
	if got := VirtualBoxSerial("CZC123", "", "VirtualBox", uuid); got != "" {
		t.Errorf("with real SSN = %q, expected empty", got)
	}
	// board serial present -> no fallback (glpi-agent requires !MSN)
	if got := VirtualBoxSerial("", "BOARD9", "VirtualBox", uuid); got != "" {
		t.Errorf("with MSN = %q, expected empty", got)
	}
	// not VirtualBox -> no fallback (don't invent serials on real hardware)
	if got := VirtualBoxSerial("", "", "Dell Inc.", uuid); got != "" {
		t.Errorf("non-VirtualBox = %q, expected empty", got)
	}
	// no uuid -> nothing to fall back to
	if got := VirtualBoxSerial("", "", "VirtualBox", ""); got != "" {
		t.Errorf("no uuid = %q, expected empty", got)
	}
}
