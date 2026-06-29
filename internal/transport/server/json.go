package server

import (
	"encoding/json"
	"strconv"
	"strings"
	"time"

	"go-fusioninventory-agent/internal/inventory"
)

// jsonMessage is the native GLPI 10+ envelope (action=inventory). The
// /front/inventory.php endpoint validates `content` against a strict JSON
// schema (inventory.schema.json).
type jsonMessage struct {
	DeviceID string      `json:"deviceid"`
	Action   string      `json:"action"`
	ItemType string      `json:"itemtype"`
	Content  jsonContent `json:"content"`
}

// jsonContent mirrors the XML Content but with JSON-correct types. Most
// sections reuse the json-tagged XML structs as-is; cpus and networks need
// dedicated structs because the schema requires integers/booleans where the XML
// (typeless) model carries strings.
type jsonContent struct {
	Hardware        *xmlHardware  `json:"hardware,omitempty"`
	BIOS            *xmlBIOS      `json:"bios,omitempty"`
	OperatingSystem *xmlOS        `json:"operatingsystem,omitempty"`
	CPUs            []jsonCPU     `json:"cpus,omitempty"`
	Memories        []xmlMemory   `json:"memories,omitempty"`
	Drives          []xmlDrive    `json:"drives,omitempty"`
	Storages        []xmlStorage  `json:"storages,omitempty"`
	Networks        []jsonNetwork `json:"networks,omitempty"`
	Softwares       []xmlSoftware `json:"softwares,omitempty"`
	USBDevices      []xmlUSB      `json:"usbdevices,omitempty"`
	LocalUsers      []xmlLUser    `json:"local_users,omitempty"`
	LocalGroups     []xmlLGroup   `json:"local_groups,omitempty"`
	Users           []xmlUser     `json:"users,omitempty"`
	Processes       []xmlProcess  `json:"processes,omitempty"`
	AccountInfo     []xmlAccount  `json:"accountinfo,omitempty"`
	VersionClient   string        `json:"versionclient,omitempty"`
}

// jsonCPU is the CPU section with stepping as an integer (schema requirement).
type jsonCPU struct {
	Name         string `json:"name,omitempty"`
	Manufacturer string `json:"manufacturer,omitempty"`
	Speed        int    `json:"speed,omitempty"`
	Core         int    `json:"core,omitempty"`
	Thread       int    `json:"thread,omitempty"`
	Arch         string `json:"arch,omitempty"`
	CoreCount    int    `json:"corecount,omitempty"`
	ID           string `json:"id,omitempty"`
	Stepping     int    `json:"stepping,omitempty"`
	FamilyNumber string `json:"familynumber,omitempty"`
	Model        string `json:"model,omitempty"`
}

// jsonNetwork is the network section with virtualdev as boolean and mtu as
// integer, and with status/type normalized to the schema's enums.
type jsonNetwork struct {
	Description string `json:"description,omitempty"`
	Type        string `json:"type,omitempty"`
	Speed       string `json:"speed,omitempty"`
	MACAddr     string `json:"macaddr,omitempty"`
	Status      string `json:"status,omitempty"`
	VirtualDev  bool   `json:"virtualdev,omitempty"`
	IPAddress   string `json:"ipaddress,omitempty"`
	IPMask      string `json:"ipmask,omitempty"`
	IPSubnet    string `json:"ipsubnet,omitempty"`
	IPGateway   string `json:"ipgateway,omitempty"`
	IPAddress6  string `json:"ipaddress6,omitempty"`
	MTU         int    `json:"mtu,omitempty"`
	Driver      string `json:"driver,omitempty"`
}

// BuildInventoryJSON serializes the inventory into GLPI's native JSON. The
// schema is stricter than the legacy XML, so dates, arch values and a few typed
// fields are normalized here.
func BuildInventoryJSON(inv *inventory.Inventory) ([]byte, error) {
	c := BuildRequest(inv).Content
	normalizeDates(&c)
	normalizeArch(&c)
	// GLPI's schema requires operatingsystem.timezone.name when the timezone
	// object is present; drop a nameless timezone (e.g. minimal hosts where only
	// the UTC offset was resolved) rather than fail validation.
	if c.OperatingSystem != nil && c.OperatingSystem.Timezone != nil && c.OperatingSystem.Timezone.Name == "" {
		c.OperatingSystem.Timezone = nil
	}

	jc := jsonContent{
		Hardware:        c.Hardware,
		BIOS:            c.BIOS,
		OperatingSystem: c.OperatingSystem,
		CPUs:            toJSONCPUs(c.CPUs),
		Memories:        c.Memories,
		Drives:          c.Drives,
		Storages:        c.Storages,
		Networks:        toJSONNetworks(c.Networks),
		Softwares:       c.Softwares,
		USBDevices:      c.USBDevices,
		LocalUsers:      c.LocalUsers,
		LocalGroups:     c.LocalGroups,
		Users:           c.Users,
		Processes:       c.Processes,
		VersionClient:   c.VersionClient,
	}
	if c.AccountInfo != nil {
		jc.AccountInfo = []xmlAccount{*c.AccountInfo}
	}

	msg := jsonMessage{
		DeviceID: inv.DeviceID,
		Action:   "inventory",
		ItemType: "Computer",
		Content:  jc,
	}
	return json.Marshal(msg)
}

// contactMessage is the native CONTACT request. The server replies with the
// supported tasks/scheduling.
type contactMessage struct {
	DeviceID       string   `json:"deviceid"`
	Action         string   `json:"action"`
	Name           string   `json:"name,omitempty"`
	Tag            string   `json:"tag,omitempty"`
	InstalledTasks []string `json:"installed-tasks"`
	EnabledTasks   []string `json:"enabled-tasks"`
}

// BuildContactJSON serializes the CONTACT message. In v1 the agent only runs
// the inventory task.
func BuildContactJSON(deviceID, name, tag string) ([]byte, error) {
	msg := contactMessage{
		DeviceID:       deviceID,
		Action:         "contact",
		Name:           name,
		Tag:            tag,
		InstalledTasks: []string{"inventory"},
		EnabledTasks:   []string{"inventory"},
	}
	return json.Marshal(msg)
}

func toJSONCPUs(in []xmlCPU) []jsonCPU {
	out := make([]jsonCPU, 0, len(in))
	for _, c := range in {
		out = append(out, jsonCPU{
			Name: c.Name, Manufacturer: c.Manufacturer, Speed: c.Speed,
			Core: c.Core, Thread: c.Thread, Arch: c.Arch, CoreCount: c.CoreCount,
			ID: c.ID, Stepping: atoiZero(c.Stepping), FamilyNumber: c.FamilyNumber, Model: c.Model,
		})
	}
	return out
}

func toJSONNetworks(in []xmlNetwork) []jsonNetwork {
	out := make([]jsonNetwork, 0, len(in))
	for _, n := range in {
		out = append(out, jsonNetwork{
			Description: n.Description, Type: netType(n.Type), Speed: n.Speed, MACAddr: n.MACAddr,
			Status: strings.ToLower(n.Status), VirtualDev: n.VirtualDev == "1",
			IPAddress: n.IPAddress, IPMask: n.IPMask, IPSubnet: n.IPSubnet, IPGateway: n.IPGateway,
			IPAddress6: n.IPAddress6, MTU: atoiZero(n.MTU), Driver: n.Driver,
		})
	}
	return out
}

// netType maps internal interface types to the schema enum. "virtual" is not a
// schema value; virtual interfaces are reported as ethernet with virtualdev set.
func netType(t string) string {
	if t == "virtual" {
		return "ethernet"
	}
	return t
}

// normalizeDates rewrites date fields that the legacy XML tolerates but the
// GLPI JSON schema rejects (it requires ISO 8601). Unparseable dates are
// dropped (omitempty) rather than failing schema validation.
func normalizeDates(c *Content) {
	if c.BIOS != nil {
		c.BIOS.BDate = isoDate(c.BIOS.BDate)
	}
	for i := range c.Softwares {
		c.Softwares[i].InstallDate = isoDateKeep(c.Softwares[i].InstallDate)
	}
	if c.OperatingSystem != nil {
		c.OperatingSystem.InstallDate = isoDateKeep(c.OperatingSystem.InstallDate)
	}
}

// archGLPI maps Go's runtime arch names to the canonical values accepted by the
// GLPI inventory schema (^(mips|...|x86_64|...|aarch64)$). Unknown values pass
// through unchanged.
var archGLPI = map[string]string{
	"amd64": "x86_64",
	"386":   "i686",
	"arm64": "aarch64",
}

// normalizeArch rewrites CPU and OS arch values to the GLPI canonical form.
func normalizeArch(c *Content) {
	fix := func(s string) string {
		if v, ok := archGLPI[s]; ok {
			return v
		}
		return s
	}
	for i := range c.CPUs {
		c.CPUs[i].Arch = fix(c.CPUs[i].Arch)
	}
	if c.OperatingSystem != nil {
		c.OperatingSystem.Arch = fix(c.OperatingSystem.Arch)
	}
}

// dateLayouts are the non-ISO date formats seen in the wild (dmidecode BIOS
// date is MM/DD/YYYY).
var dateLayouts = []string{"01/02/2006", "2006/01/02", "01/02/06"}

// isoDate converts a date to YYYY-MM-DD, returning "" if it cannot be parsed.
func isoDate(s string) string {
	s = strings.TrimSpace(s)
	if s == "" {
		return ""
	}
	if _, err := time.Parse("2006-01-02", s); err == nil {
		return s
	}
	for _, layout := range dateLayouts {
		if tm, err := time.Parse(layout, s); err == nil {
			return tm.Format("2006-01-02")
		}
	}
	return ""
}

// isoDateKeep is like isoDate but keeps the original value when it cannot be
// parsed (some fields, e.g. already-ISO datetimes, are valid as-is).
func isoDateKeep(s string) string {
	if iso := isoDate(s); iso != "" {
		return iso
	}
	return strings.TrimSpace(s)
}

// atoiZero parses an integer, returning 0 when the string is empty or invalid
// (0 is dropped by omitempty).
func atoiZero(s string) int {
	n, _ := strconv.Atoi(strings.TrimSpace(s))
	return n
}
