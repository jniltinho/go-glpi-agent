package server

import (
	"encoding/xml"
	"strings"
	"testing"

	"go-fusioninventory-agent/internal/inventory"
)

// TestSerializeStructure verifies that the generated XML has the correct
// envelope and includes the sections/fields expected by GLPI.
func TestSerializeStructure(t *testing.T) {
	inv := inventory.New("host-2026-06-29-10-00-00")
	inv.Tag = "entidade1"
	inv.SetHardware(func(h *inventory.Hardware) {
		h.Name = "host"
		h.Memory = 16000
	})
	inv.SetOperatingSystem(func(o *inventory.OperatingSystem) {
		o.Name = "ubuntu"
		o.FullName = "Ubuntu 24.04 LTS"
		o.TimezoneName = "America/Sao_Paulo"
		o.TimezoneUTCO = "-0300"
	})
	inv.AddCPU(inventory.CPU{Name: "Intel", Core: 4, Thread: 8})
	inv.AddSoftware(inventory.Software{Name: "vim", Version: "9.0", From: "dpkg"})
	inv.AddNetwork(inventory.Network{Description: "eth0", IPAddress: "10.0.0.5"})

	out, err := Serialize(inv)
	if err != nil {
		t.Fatalf("Serialize: %v", err)
	}
	s := string(out)

	if !strings.HasPrefix(s, xml.Header) {
		t.Error("missing XML header")
	}
	for _, want := range []string{
		"<REQUEST>", "<QUERY>INVENTORY</QUERY>",
		"<DEVICEID>host-2026-06-29-10-00-00</DEVICEID>",
		"<CONTENT>", "<HARDWARE>", "<NAME>host</NAME>",
		"<OPERATINGSYSTEM>", "<TIMEZONE>", "<NAME>America/Sao_Paulo</NAME>",
		"<CPUS>", "<SOFTWARES>", "<NETWORKS>",
		"<KEYNAME>TAG</KEYNAME>", "<KEYVALUE>entidade1</KEYVALUE>",
		"FusionInventory-Agent",
	} {
		if !strings.Contains(s, want) {
			t.Errorf("XML does not contain %q", want)
		}
	}
}

// TestSerializeRoundTrip ensures the generated XML can be parsed back.
func TestSerializeRoundTrip(t *testing.T) {
	inv := inventory.New("dev-1")
	inv.AddCPU(inventory.CPU{Name: "Test CPU", Core: 2})

	out, err := Serialize(inv)
	if err != nil {
		t.Fatalf("Serialize: %v", err)
	}

	var req Request
	if err := xml.Unmarshal(out, &req); err != nil {
		t.Fatalf("Unmarshal: %v", err)
	}
	if req.DeviceID != "dev-1" {
		t.Errorf("DeviceID = %q, expected dev-1", req.DeviceID)
	}
	if req.Query != "INVENTORY" {
		t.Errorf("Query = %q, expected INVENTORY", req.Query)
	}
	if len(req.Content.CPUs) != 1 || req.Content.CPUs[0].Name != "Test CPU" {
		t.Errorf("CPU did not round-trip correctly: %+v", req.Content.CPUs)
	}
}

// TestEmptyFieldsOmitted verifies that empty fields do not appear in the XML.
func TestEmptyFieldsOmitted(t *testing.T) {
	inv := inventory.New("dev-2")
	out, _ := Serialize(inv)
	s := string(out)

	if strings.Contains(s, "<BIOS>") {
		t.Error("empty BIOS should not appear")
	}
	if strings.Contains(s, "<SOFTWARES>") {
		t.Error("empty SOFTWARES should not appear")
	}
}
