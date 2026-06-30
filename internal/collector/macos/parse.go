// Pure, OS-independent parsers for the macOS collectors. No build tag and no
// macOS-only calls, so the logic is unit-testable on any platform (the command
// I/O — system_profiler, ioreg, sysctl, route — lives in the build-tagged
// collector files). system_profiler is decoded from its `-json` output.

package macos

import (
	"encoding/json"
	"strconv"
	"strings"

	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// firstLine returns the first non-empty trimmed line of s, or "".
func firstLine(s string) string {
	for _, line := range sysutil.SplitLines(s) {
		if t := strings.TrimSpace(line); t != "" {
			return t
		}
	}
	return ""
}

// --- system_profiler -json helpers -----------------------------------------

// spItems decodes `system_profiler -json <Type>` output and returns the array
// stored under the data-type key (e.g. "SPHardwareDataType") as generic maps.
func spItems(data []byte, dataType string) []map[string]any {
	var root map[string]json.RawMessage
	if err := json.Unmarshal(data, &root); err != nil {
		return nil
	}
	raw, ok := root[dataType]
	if !ok {
		return nil
	}
	var arr []map[string]any
	if err := json.Unmarshal(raw, &arr); err != nil {
		return nil
	}
	return arr
}

// mapStr returns m[key] as a trimmed string, trying each key in order; "" if none.
func mapStr(m map[string]any, keys ...string) string {
	for _, k := range keys {
		if v, ok := m[k]; ok {
			if s, ok := v.(string); ok && strings.TrimSpace(s) != "" {
				return strings.TrimSpace(s)
			}
		}
	}
	return ""
}

// mapInt64 returns m[key] as an int64 (accepting JSON numbers and numeric
// strings), trying each key in order; 0 if none.
func mapInt64(m map[string]any, keys ...string) int64 {
	for _, k := range keys {
		switch v := m[k].(type) {
		case float64:
			return int64(v)
		case string:
			if n, err := strconv.ParseInt(strings.TrimSpace(v), 10, 64); err == nil {
				return n
			}
		}
	}
	return 0
}

// subItems returns the nested "_items" array of m as generic maps, or nil.
func subItems(m map[string]any) []map[string]any {
	raw, ok := m["_items"].([]any)
	if !ok {
		return nil
	}
	out := make([]map[string]any, 0, len(raw))
	for _, e := range raw {
		if mm, ok := e.(map[string]any); ok {
			out = append(out, mm)
		}
	}
	return out
}

// --- SPHardwareDataType (system identity / CPU name) -----------------------

// spHardware holds the fields of the SPHardwareDataType "hardware_overview".
type spHardware struct {
	ChipType       string // Apple Silicon, e.g. "Apple M1 Pro"
	CPUType        string // Intel, e.g. "Quad-Core Intel Core i7"
	SerialNumber   string
	PlatformUUID   string
	MachineModel   string // model identifier, e.g. "MacBookPro18,2"
	MachineName    string // e.g. "MacBook Pro"
	BootROMVersion string
}

// parseSPHardware decodes SPHardwareDataType JSON into spHardware.
func parseSPHardware(data []byte) spHardware {
	items := spItems(data, "SPHardwareDataType")
	if len(items) == 0 {
		return spHardware{}
	}
	m := items[0]
	return spHardware{
		ChipType:       mapStr(m, "chip_type"),
		CPUType:        mapStr(m, "cpu_type"),
		SerialNumber:   mapStr(m, "serial_number"),
		PlatformUUID:   mapStr(m, "platform_UUID"),
		MachineModel:   mapStr(m, "machine_model"),
		MachineName:    mapStr(m, "machine_name"),
		BootROMVersion: mapStr(m, "boot_rom_version", "os_loader_version"),
	}
}

// --- SPMemoryDataType ------------------------------------------------------

// parseSPMemory decodes SPMemoryDataType into per-module entries. Intel reports a
// list of banks (often under "_items"); Apple Silicon reports a single unified
// module. Empty banks ("empty"/"") are skipped.
func parseSPMemory(data []byte) []inventory.Memory {
	items := spItems(data, "SPMemoryDataType")
	var out []inventory.Memory
	slot := 0
	add := func(m map[string]any, name string) {
		size := mapStr(m, "dimm_size", "SPMemoryDataType")
		typ := mapStr(m, "dimm_type")
		if strings.EqualFold(size, "empty") || (size == "" && typ == "") {
			return
		}
		slot++
		out = append(out, inventory.Memory{
			Capacity:     memSizeMB(size),
			Type:         typ,
			Speed:        mapStr(m, "dimm_speed"),
			Manufacturer: sysutil.CleanDMI(mapStr(m, "dimm_manufacturer")),
			SerialNumber: sysutil.CleanDMI(mapStr(m, "dimm_serial_number")),
			Caption:      name,
			Description:  name,
			NumSlots:     slot,
		})
	}
	for _, it := range items {
		if banks := subItems(it); len(banks) > 0 {
			for _, b := range banks {
				add(b, mapStr(b, "_name"))
			}
			continue
		}
		add(it, mapStr(it, "_name"))
	}
	return out
}

// memSizeMB converts a system_profiler memory size ("16 GB", "8192 MB") to MB.
func memSizeMB(s string) int {
	f := strings.Fields(s)
	if len(f) == 0 {
		return 0
	}
	n, err := strconv.Atoi(f[0])
	if err != nil {
		return 0
	}
	if len(f) >= 2 {
		switch strings.ToUpper(f[1]) {
		case "GB":
			return n * 1024
		case "TB":
			return n * 1024 * 1024
		}
	}
	return n // assume MB
}

// --- SPNVMeDataType / SPSerialATADataType (physical disks) -----------------

// parseSPStorage decodes a storage data type (SPNVMeDataType or
// SPSerialATADataType) into Storage entries. Drives are nested under each
// controller's "_items"; the controller node itself is skipped.
func parseSPStorage(data []byte, dataType, typeLabel string) []inventory.Storage {
	items := spItems(data, dataType)
	var out []inventory.Storage
	var walk func(nodes []map[string]any)
	walk = func(nodes []map[string]any) {
		for _, n := range nodes {
			if children := subItems(n); len(children) > 0 {
				walk(children)
				continue
			}
			name := mapStr(n, "_name")
			sizeBytes := mapInt64(n, "size_in_bytes")
			model := mapStr(n, "device_model", "_name")
			serial := mapStr(n, "device_serial", "spnvme_device_serial", "device_serial_number")
			if name == "" && model == "" && sizeBytes == 0 {
				continue
			}
			diskName := name
			if bsd := mapStr(n, "bsd_name"); bsd != "" {
				diskName = "/dev/" + bsd
			}
			out = append(out, inventory.Storage{
				Name:         diskName,
				Model:        model,
				Description:  name,
				Type:         typeLabel,
				DiskSize:     int(sizeBytes / 1024 / 1024),
				SerialNumber: sysutil.CleanDMI(serial),
				Firmware:     mapStr(n, "device_revision"),
			})
		}
	}
	walk(items)
	return out
}

// --- SPUSBDataType ---------------------------------------------------------

// parseSPUSB decodes SPUSBDataType into USB devices, recursing through the hub
// tree. Hubs (name contains "Hub", or no vendor/product id) are skipped.
func parseSPUSB(data []byte) []inventory.USBDevice {
	items := spItems(data, "SPUSBDataType")
	var out []inventory.USBDevice
	var walk func(nodes []map[string]any)
	walk = func(nodes []map[string]any) {
		for _, n := range nodes {
			children := subItems(n)
			name := mapStr(n, "_name")
			vid := usbHexID(mapStr(n, "vendor_id"))
			pid := usbHexID(mapStr(n, "product_id"))
			isHub := strings.Contains(strings.ToLower(name), "hub")
			if vid != "" && pid != "" && !isHub {
				out = append(out, inventory.USBDevice{
					VendorID:     vid,
					ProductID:    pid,
					Manufacturer: usbVendorName(mapStr(n, "manufacturer", "vendor_id")),
					Name:         name,
					Caption:      name,
					Serial:       sysutil.CleanDMI(mapStr(n, "serial_num")),
				})
			}
			if len(children) > 0 {
				walk(children)
			}
		}
	}
	walk(items)
	return out
}

// usbHexID extracts the 4-hex-digit id from system_profiler's vendor/product
// field, e.g. "0x05ac  (Apple Inc.)" -> "05ac"; returns "" if no hex present.
func usbHexID(s string) string {
	f := strings.Fields(s)
	if len(f) == 0 {
		return ""
	}
	return strings.ToLower(strings.TrimPrefix(f[0], "0x"))
}

// usbVendorName returns the parenthesized vendor name from a vendor_id field
// ("0x05ac  (Apple Inc.)" -> "Apple Inc."), or the raw string if already a name.
func usbVendorName(s string) string {
	if i := strings.IndexByte(s, '('); i >= 0 {
		if j := strings.IndexByte(s[i:], ')'); j > 0 {
			return strings.TrimSpace(s[i+1 : i+j])
		}
	}
	if strings.HasPrefix(s, "0x") {
		return ""
	}
	return s
}

// --- SPApplicationsDataType ------------------------------------------------

// parseSPApplications decodes SPApplicationsDataType into Software entries.
func parseSPApplications(data []byte) []inventory.Software {
	items := spItems(data, "SPApplicationsDataType")
	var out []inventory.Software
	for _, m := range items {
		name := mapStr(m, "_name")
		if name == "" {
			continue
		}
		out = append(out, inventory.Software{
			Name:        name,
			Version:     mapStr(m, "version"),
			Publisher:   mapStr(m, "obtained_from", "signed_by"),
			InstallDate: mapStr(m, "lastModified"),
			Arch:        mapStr(m, "arch_kind"),
			From:        "system_profiler",
		})
	}
	return out
}

// --- ioreg (IOPlatformExpertDevice) ----------------------------------------

// ioPlatform holds the identity fields read from
// `ioreg -d2 -c IOPlatformExpertDevice`.
type ioPlatform struct {
	Serial       string
	UUID         string
	Manufacturer string
	Model        string
}

// parseIOReg parses ioreg output for the IOPlatformExpertDevice properties
// IOPlatformSerialNumber, IOPlatformUUID, manufacturer and model. Lines look like
//
//	"IOPlatformSerialNumber" = "C02XXXXX"
//	"manufacturer" = <"Apple Inc.">
func parseIOReg(out string) ioPlatform {
	var p ioPlatform
	for _, line := range sysutil.SplitLines(out) {
		key, val, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}
		k := strings.Trim(strings.TrimSpace(key), `"`)
		v := ioregValue(val)
		switch k {
		case "IOPlatformSerialNumber":
			p.Serial = v
		case "IOPlatformUUID":
			p.UUID = v
		case "manufacturer":
			p.Manufacturer = v
		case "model":
			p.Model = v
		}
	}
	return p
}

// ioregValue strips ioreg's quoting/angle-bracket wrapping from a property value,
// e.g. `<"Apple Inc.">` -> `Apple Inc.` and `"C02XX"` -> `C02XX`.
func ioregValue(s string) string {
	s = strings.TrimSpace(s)
	s = strings.TrimPrefix(s, "<")
	s = strings.TrimSuffix(s, ">")
	s = strings.Trim(s, `"`)
	return strings.TrimSpace(s)
}

// --- identity resolution (serial / UUID fallback chain) --------------------

// resolveIdentity builds the BIOS struct and the UUID from the system_profiler
// overview with ioreg fallbacks, applying the serial chain and the
// serial-of-last-resort = UUID rule. Pure (no I/O) so it is unit-tested on any
// platform; mirrors the official agent's MacOS/Bios.pm + MacOS/Hardware.pm.
//
//	serial: system_profiler serial_number -> ioreg IOPlatformSerialNumber -> UUID
//	uuid:   system_profiler platform_UUID -> ioreg IOPlatformUUID
func resolveIdentity(hw spHardware, io ioPlatform) (inventory.BIOS, string) {
	c := sysutil.CleanDMI

	uuid := c(hw.PlatformUUID)
	if uuid == "" {
		uuid = c(io.UUID)
	}

	ssn := c(hw.SerialNumber)
	if ssn == "" {
		ssn = c(io.Serial)
	}
	if ssn == "" && uuid != "" {
		ssn = uuid // serial of last resort, so the host is never serial-less
	}

	manufacturer := c(io.Manufacturer)
	if manufacturer == "" {
		manufacturer = "Apple Inc."
	}

	model := hw.MachineModel
	if model == "" {
		model = hw.MachineName
	}
	if model == "" {
		model = c(io.Model)
	}

	return inventory.BIOS{
		SManufacturer: manufacturer,
		SModel:        model,
		SSN:           ssn,
		BManufacturer: manufacturer,
		BVersion:      hw.BootROMVersion,
	}, uuid
}

// --- route -----------------------------------------------------------------

// parseRouteGateway extracts the gateway from `route -n get default` output
// (the line "    gateway: 192.168.1.1").
func parseRouteGateway(out string) string {
	for _, line := range sysutil.SplitLines(out) {
		t := strings.TrimSpace(line)
		if v, ok := strings.CutPrefix(t, "gateway:"); ok {
			return strings.TrimSpace(v)
		}
	}
	return ""
}
