// Pure, OS-independent parsers for the FreeBSD collectors. No build tag and no
// FreeBSD-only calls, so the logic is unit-testable on any platform (the
// command/sysctl I/O lives in the build-tagged collector files).

package freebsd

import (
	"strconv"
	"strings"

	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// parseKenv parses `kenv` output (lines of key="value") into a map. Values are
// unquoted. Used to read the smbios.* keys.
func parseKenv(out string) map[string]string {
	m := map[string]string{}
	for _, line := range sysutil.SplitLines(out) {
		key, val, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}
		key = strings.TrimSpace(key)
		val = strings.Trim(strings.TrimSpace(val), `"`)
		if key != "" {
			m[key] = val
		}
	}
	return m
}

// biosFromKenv maps the smbios.* kenv keys to a BIOS struct and the system UUID,
// running every identity string through the shared junk filter.
func biosFromKenv(kv map[string]string) (inventory.BIOS, string) {
	c := sysutil.CleanDMI
	b := inventory.BIOS{
		SManufacturer: c(kv["smbios.system.maker"]),
		SModel:        c(kv["smbios.system.product"]),
		SSN:           c(kv["smbios.system.serial"]),
		BManufacturer: c(kv["smbios.bios.vendor"]),
		BVersion:      kv["smbios.bios.version"],
		BDate:         kv["smbios.bios.reldate"],
		MManufacturer: c(kv["smbios.planar.maker"]),
		MModel:        c(kv["smbios.planar.product"]),
		MSN:           c(kv["smbios.planar.serial"]),
	}
	return b, c(kv["smbios.system.uuid"])
}

// geomDisk is one physical disk parsed from `geom disk list`.
type geomDisk struct {
	Name      string
	Descr     string // model
	Ident     string // serial
	Mediasize int64  // bytes
}

// parseGeomDiskList parses `geom disk list` into per-disk records. The output is
// block-structured: a "Geom name: <dev>" line starts a disk, followed by indented
// "Mediasize:", "descr:" and "ident:" fields.
func parseGeomDiskList(out string) []geomDisk {
	var disks []geomDisk
	var cur *geomDisk
	flush := func() {
		if cur != nil {
			disks = append(disks, *cur)
			cur = nil
		}
	}
	for _, line := range sysutil.SplitLines(out) {
		t := strings.TrimSpace(line)
		switch {
		case strings.HasPrefix(t, "Geom name:"):
			flush()
			cur = &geomDisk{Name: strings.TrimSpace(strings.TrimPrefix(t, "Geom name:"))}
		case cur == nil:
			continue
		case strings.HasPrefix(t, "Mediasize:"):
			f := strings.Fields(strings.TrimPrefix(t, "Mediasize:"))
			if len(f) > 0 {
				cur.Mediasize, _ = strconv.ParseInt(f[0], 10, 64)
			}
		case strings.HasPrefix(t, "descr:"):
			cur.Descr = strings.TrimSpace(strings.TrimPrefix(t, "descr:"))
		case strings.HasPrefix(t, "ident:"):
			id := strings.TrimSpace(strings.TrimPrefix(t, "ident:"))
			if id != "(null)" {
				cur.Ident = id
			}
		}
	}
	flush()
	return disks
}

// parsePkgQuery parses `pkg query "%n\t%v\t%q\t%sb\t%c"` output into software
// entries (name, version, arch/ABI, size in bytes, comment).
func parsePkgQuery(out string) []inventory.Software {
	var sw []inventory.Software
	for _, line := range sysutil.SplitLines(out) {
		f := strings.Split(line, "\t")
		if len(f) < 2 || f[0] == "" {
			continue
		}
		s := inventory.Software{Name: f[0], Version: f[1], From: "pkg"}
		if len(f) > 2 {
			s.Arch = f[2]
		}
		if len(f) > 3 {
			s.FileSize, _ = strconv.ParseInt(strings.TrimSpace(f[3]), 10, 64)
		}
		if len(f) > 4 {
			s.Comments = f[4]
		}
		sw = append(sw, s)
	}
	return sw
}

// parseRouteGateway extracts the gateway from `route -n get default` output
// (the line "    gateway: 10.0.2.2").
func parseRouteGateway(out string) string {
	for _, line := range sysutil.SplitLines(out) {
		t := strings.TrimSpace(line)
		if v, ok := strings.CutPrefix(t, "gateway:"); ok {
			return strings.TrimSpace(v)
		}
	}
	return ""
}

// parseUSBDesc parses `usbconfig -d <dev> dump_device_desc` output into a USB
// device, also reporting whether it is a hub (bDeviceClass 0x09). Lines look like
// "  idVendor = 0x80ee " and "  iProduct = 0x0002  <USB Tablet>".
func parseUSBDesc(out string) (inventory.USBDevice, bool) {
	var u inventory.USBDevice
	isHub := false
	for _, line := range sysutil.SplitLines(out) {
		key, val, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}
		key = strings.TrimSpace(key)
		val = strings.TrimSpace(val)
		hex := firstField(val)         // "0x80ee"
		name := angleBracketValue(val) // "<USB Tablet>" -> "USB Tablet"
		switch key {
		case "idVendor":
			u.VendorID = trimHex(hex)
		case "idProduct":
			u.ProductID = trimHex(hex)
		case "bDeviceClass":
			if hex == "0x0009" || hex == "0x09" {
				isHub = true
			}
		case "iManufacturer":
			u.Manufacturer = usbString(name)
		case "iProduct":
			u.Name = usbString(name)
			u.Caption = usbString(name)
		case "iSerialNumber":
			u.Serial = usbString(name)
		}
	}
	return u, isHub
}

// firstField returns the first whitespace-separated token of s.
func firstField(s string) string {
	if f := strings.Fields(s); len(f) > 0 {
		return f[0]
	}
	return ""
}

// angleBracketValue returns the text inside the first <...> of s, or "".
func angleBracketValue(s string) string {
	i := strings.IndexByte(s, '<')
	j := strings.IndexByte(s, '>')
	if i >= 0 && j > i {
		return s[i+1 : j]
	}
	return ""
}

// trimHex lowercases a "0x80ee" hex string to "80ee".
func trimHex(s string) string {
	return strings.ToLower(strings.TrimPrefix(s, "0x"))
}

// usbString drops usbconfig's placeholder descriptor strings.
func usbString(s string) string {
	if s == "no string" || s == "" {
		return ""
	}
	return s
}
