package freebsd

import "testing"

func TestParseKenvAndBIOS(t *testing.T) {
	out := `smbios.bios.reldate="12/01/2006"
smbios.bios.vendor="innotek GmbH"
smbios.bios.version="VirtualBox"
smbios.system.maker="innotek GmbH"
smbios.system.product="VirtualBox"
smbios.system.serial="0"
smbios.system.uuid="9100abc9-c47b-446b-9ede-367ffd95f2b8"
smbios.planar.maker="Oracle Corporation"
smbios.planar.product="VirtualBox"
smbios.planar.serial="0"
unrelated.key="ignored"`
	b, uuid := biosFromKenv(parseKenv(out))
	if b.SManufacturer != "innotek GmbH" || b.SModel != "VirtualBox" {
		t.Errorf("system = %q / %q", b.SManufacturer, b.SModel)
	}
	if b.BVersion != "VirtualBox" || b.BDate != "12/01/2006" {
		t.Errorf("bios = %q / %q", b.BVersion, b.BDate)
	}
	if b.SSN != "" { // serial "0" is junk -> filtered
		t.Errorf("SSN = %q, expected empty (junk '0')", b.SSN)
	}
	if uuid != "9100abc9-c47b-446b-9ede-367ffd95f2b8" {
		t.Errorf("uuid = %q", uuid)
	}
}

func TestParsePkgQuery(t *testing.T) {
	out := "bash\t5.2.21\tFreeBSD:14:amd64\t8388608\tThe GNU Bourne Again SHell\n" +
		"\t\t\t\t\n" + // blank/garbage line skipped
		"curl\t8.7.1\tFreeBSD:14:amd64\t4194304\tNon-interactive tool to get files"
	sw := parsePkgQuery(out)
	if len(sw) != 2 {
		t.Fatalf("got %d packages, want 2", len(sw))
	}
	if sw[0].Name != "bash" || sw[0].Version != "5.2.21" || sw[0].From != "pkg" {
		t.Errorf("pkg[0] = %+v", sw[0])
	}
	if sw[0].Arch != "FreeBSD:14:amd64" || sw[0].FileSize != 8388608 {
		t.Errorf("pkg[0] arch/size = %q / %d", sw[0].Arch, sw[0].FileSize)
	}
}

func TestParseGeomDiskList(t *testing.T) {
	out := `Geom name: ada0
Providers:
1. Name: ada0
   Mediasize: 107374182400 (100G)
   Sectorsize: 512
   descr: VBOX HARDDISK
   ident: (null)
   rotationrate: unknown

Geom name: ada1
Providers:
1. Name: ada1
   Mediasize: 53687091200 (50G)
   descr: Samsung SSD 870
   ident: S5_SERIAL_123
`
	disks := parseGeomDiskList(out)
	if len(disks) != 2 {
		t.Fatalf("got %d disks, want 2", len(disks))
	}
	if disks[0].Name != "ada0" || disks[0].Descr != "VBOX HARDDISK" || disks[0].Mediasize != 107374182400 {
		t.Errorf("disk0 = %+v", disks[0])
	}
	if disks[0].Ident != "" { // "(null)" -> empty
		t.Errorf("disk0 ident = %q, expected empty", disks[0].Ident)
	}
	if disks[1].Ident != "S5_SERIAL_123" {
		t.Errorf("disk1 ident = %q", disks[1].Ident)
	}
}

func TestParseRouteGateway(t *testing.T) {
	out := `   route to: default
destination: default
       gateway: 10.0.2.2
         flags: <UP,GATEWAY,DONE,STATIC>`
	if gw := parseRouteGateway(out); gw != "10.0.2.2" {
		t.Errorf("gateway = %q, want 10.0.2.2", gw)
	}
}

func TestParseUSBDesc(t *testing.T) {
	tablet := `ugen0.2: <USB Tablet VirtualBox> at usbus0
  bDeviceClass = 0x0000
  idVendor = 0x80ee
  idProduct = 0x0021
  iManufacturer = 0x0003  <VirtualBox>
  iProduct = 0x0002  <USB Tablet>
  iSerialNumber = 0x0000  <no string>`
	u, isHub := parseUSBDesc(tablet)
	if isHub {
		t.Error("tablet wrongly classified as hub")
	}
	if u.VendorID != "80ee" || u.ProductID != "0021" {
		t.Errorf("vid/pid = %q/%q", u.VendorID, u.ProductID)
	}
	if u.Name != "USB Tablet" || u.Manufacturer != "VirtualBox" {
		t.Errorf("name/mfr = %q/%q", u.Name, u.Manufacturer)
	}
	if u.Serial != "" { // "no string" -> empty
		t.Errorf("serial = %q, expected empty", u.Serial)
	}

	hub := "ugen0.1: <hub>\n  bDeviceClass = 0x0009\n  idVendor = 0x0000\n  idProduct = 0x0000"
	if _, isHub := parseUSBDesc(hub); !isHub {
		t.Error("hub not detected (bDeviceClass 0x0009)")
	}
}
