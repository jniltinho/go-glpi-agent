package server

import (
	"encoding/json"
	"testing"

	"go-glpi-agent/internal/inventory"
)

// TestBuildInventoryJSON checks the normalizations the GLPI native schema
// requires but the legacy XML tolerates: ISO dates, canonical arch, lowercase
// network status, enum-mapped type, and the string->integer/boolean conversions
// for stepping, mtu and virtualdev.
func TestBuildInventoryJSON(t *testing.T) {
	inv := inventory.New("dev-1")
	inv.AgentID = "agent-uuid"
	inv.SetBIOS(func(b *inventory.BIOS) { b.BDate = "07/11/2025" }) // MM/DD/YYYY
	inv.AddCPU(inventory.CPU{Arch: "amd64", Stepping: "5"})
	inv.AddNetwork(inventory.Network{Status: "Up", VirtualDev: "1", MTU: "65536", Type: "virtual"})

	raw, err := BuildInventoryJSON(inv)
	if err != nil {
		t.Fatalf("BuildInventoryJSON: %v", err)
	}

	var msg struct {
		DeviceID string `json:"deviceid"`
		Action   string `json:"action"`
		ItemType string `json:"itemtype"`
		Content  struct {
			BIOS struct {
				BDate string `json:"bdate"`
			} `json:"bios"`
			CPUs []struct {
				Arch     string `json:"arch"`
				Stepping int    `json:"stepping"`
			} `json:"cpus"`
			Networks []struct {
				Status     string `json:"status"`
				Type       string `json:"type"`
				VirtualDev bool   `json:"virtualdev"`
				MTU        int    `json:"mtu"`
			} `json:"networks"`
		} `json:"content"`
	}
	if err := json.Unmarshal(raw, &msg); err != nil {
		t.Fatalf("unmarshal: %v (typed field mismatch means a value was not converted)", err)
	}

	if msg.Action != "inventory" || msg.ItemType != "Computer" || msg.DeviceID != "dev-1" {
		t.Errorf("envelope = %+v, want action=inventory itemtype=Computer deviceid=dev-1", msg)
	}
	if msg.Content.BIOS.BDate != "2025-07-11" {
		t.Errorf("bios.bdate = %q, expected 2025-07-11", msg.Content.BIOS.BDate)
	}
	if len(msg.Content.CPUs) != 1 || msg.Content.CPUs[0].Arch != "x86_64" || msg.Content.CPUs[0].Stepping != 5 {
		t.Errorf("cpus = %+v, expected arch=x86_64 stepping=5", msg.Content.CPUs)
	}
	n := msg.Content.Networks
	if len(n) != 1 || n[0].Status != "up" || n[0].Type != "ethernet" || !n[0].VirtualDev || n[0].MTU != 65536 {
		t.Errorf("networks = %+v, expected status=up type=ethernet virtualdev=true mtu=65536", n)
	}
}

// TestBuildContactJSON checks the native CONTACT payload GLPI 11 requires:
// deviceid, action=contact, a non-empty name and version, and the single
// inventory task. A missing version makes the endpoint answer 400 "JSON not
// well formed!".
func TestBuildContactJSON(t *testing.T) {
	raw, err := BuildContactJSON("GO-GLPI-AGENT-TEST-001", "GLPI-Agent", "datacenter-1")
	if err != nil {
		t.Fatalf("BuildContactJSON: %v", err)
	}

	var msg struct {
		DeviceID       string   `json:"deviceid"`
		Action         string   `json:"action"`
		Name           string   `json:"name"`
		Tag            string   `json:"tag"`
		Version        string   `json:"version"`
		InstalledTasks []string `json:"installed-tasks"`
		EnabledTasks   []string `json:"enabled-tasks"`
	}
	if err := json.Unmarshal(raw, &msg); err != nil {
		t.Fatalf("unmarshal contact: %v", err)
	}

	if msg.DeviceID != "GO-GLPI-AGENT-TEST-001" || msg.Action != "contact" || msg.Name != "GLPI-Agent" {
		t.Errorf("envelope = %+v, want deviceid=GO-GLPI-AGENT-TEST-001 action=contact name=GLPI-Agent", msg)
	}
	if msg.Version == "" {
		t.Error("version must not be empty (GLPI 11 rejects a CONTACT without it)")
	}
	if len(msg.InstalledTasks) != 1 || msg.InstalledTasks[0] != "inventory" {
		t.Errorf("installed-tasks = %#v, want [inventory]", msg.InstalledTasks)
	}
	if len(msg.EnabledTasks) != 1 || msg.EnabledTasks[0] != "inventory" {
		t.Errorf("enabled-tasks = %#v, want [inventory]", msg.EnabledTasks)
	}
}

// TestBuildInventoryJSONDropsNamelessTimezone verifies a timezone with only an
// offset (no name) is dropped, since GLPI's schema requires timezone.name.
func TestBuildInventoryJSONDropsNamelessTimezone(t *testing.T) {
	inv := inventory.New("dev-1")
	inv.SetOperatingSystem(func(o *inventory.OperatingSystem) {
		o.Name = "linux"
		o.TimezoneUTCO = "+0000" // offset present, name empty
	})

	raw, err := BuildInventoryJSON(inv)
	if err != nil {
		t.Fatalf("BuildInventoryJSON: %v", err)
	}
	var msg struct {
		Content struct {
			OS struct {
				Timezone *struct {
					Name string `json:"name"`
				} `json:"timezone"`
			} `json:"operatingsystem"`
		} `json:"content"`
	}
	if err := json.Unmarshal(raw, &msg); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if msg.Content.OS.Timezone != nil {
		t.Errorf("timezone = %+v, expected nil (nameless timezone must be dropped)", msg.Content.OS.Timezone)
	}
}
