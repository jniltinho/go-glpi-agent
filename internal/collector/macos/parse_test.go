package macos

import "testing"

// --- SPHardwareDataType (Intel + Apple Silicon) ---

const spHardwareIntel = `{
  "SPHardwareDataType": [
    {
      "_name": "hardware_overview",
      "boot_rom_version": "1968.140.2.0.0",
      "cpu_type": "Quad-Core Intel Core i7",
      "current_processor_speed": "2.3 GHz",
      "machine_model": "MacBookPro15,1",
      "machine_name": "MacBook Pro",
      "physical_memory": "16 GB",
      "platform_UUID": "564D5A11-1111-2222-3333-444455556666",
      "serial_number": "C02ABCDEFGH"
    }
  ]
}`

const spHardwareAppleSilicon = `{
  "SPHardwareDataType": [
    {
      "_name": "hardware_overview",
      "boot_rom_version": "10151.140.19",
      "chip_type": "Apple M1 Pro",
      "machine_model": "MacBookPro18,3",
      "machine_name": "MacBook Pro",
      "number_processors": "proc 8:6:2",
      "physical_memory": "16 GB",
      "platform_UUID": "00006000-0011223344556677",
      "serial_number": "ZZZ1234567"
    }
  ]
}`

func TestParseSPHardware(t *testing.T) {
	intel := parseSPHardware([]byte(spHardwareIntel))
	if intel.CPUType != "Quad-Core Intel Core i7" || intel.ChipType != "" {
		t.Errorf("intel cpu: got chip=%q cpu=%q", intel.ChipType, intel.CPUType)
	}
	if intel.SerialNumber != "C02ABCDEFGH" || intel.MachineModel != "MacBookPro15,1" {
		t.Errorf("intel identity: %+v", intel)
	}
	if intel.BootROMVersion != "1968.140.2.0.0" {
		t.Errorf("intel boot rom: %q", intel.BootROMVersion)
	}

	as := parseSPHardware([]byte(spHardwareAppleSilicon))
	if as.ChipType != "Apple M1 Pro" {
		t.Errorf("apple silicon chip: %q", as.ChipType)
	}
	if as.PlatformUUID != "00006000-0011223344556677" {
		t.Errorf("apple silicon uuid: %q", as.PlatformUUID)
	}
}

// --- serial / UUID fallback chain ---

func TestResolveIdentity(t *testing.T) {
	tests := []struct {
		name       string
		hw         spHardware
		io         ioPlatform
		wantSerial string
		wantUUID   string
	}{
		{
			name:       "full system_profiler data",
			hw:         spHardware{SerialNumber: "C02ABCDEFGH", PlatformUUID: "UUID-1", MachineModel: "MacBookPro15,1"},
			wantSerial: "C02ABCDEFGH",
			wantUUID:   "UUID-1",
		},
		{
			name:       "serial from ioreg when system_profiler lacks it",
			hw:         spHardware{PlatformUUID: "UUID-2"},
			io:         ioPlatform{Serial: "IOREG-SERIAL"},
			wantSerial: "IOREG-SERIAL",
			wantUUID:   "UUID-2",
		},
		{
			name:       "uuid from ioreg when system_profiler lacks it",
			hw:         spHardware{SerialNumber: "S3"},
			io:         ioPlatform{UUID: "IOREG-UUID"},
			wantSerial: "S3",
			wantUUID:   "IOREG-UUID",
		},
		{
			name:       "serial redacted, UUID present -> serial falls back to UUID",
			hw:         spHardware{PlatformUUID: "UUID-4"},
			wantSerial: "UUID-4",
			wantUUID:   "UUID-4",
		},
		{
			name:       "junk/zeroed serial filtered by CleanDMI -> falls back to UUID",
			hw:         spHardware{SerialNumber: "0000000000", PlatformUUID: "UUID-5"},
			wantSerial: "UUID-5",
			wantUUID:   "UUID-5",
		},
		{
			name:       "no identity at all -> empty serial, empty uuid",
			wantSerial: "",
			wantUUID:   "",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			b, uuid := resolveIdentity(tt.hw, tt.io)
			if b.SSN != tt.wantSerial {
				t.Errorf("serial = %q, want %q", b.SSN, tt.wantSerial)
			}
			if uuid != tt.wantUUID {
				t.Errorf("uuid = %q, want %q", uuid, tt.wantUUID)
			}
			if b.SManufacturer == "" {
				t.Errorf("manufacturer should never be empty (got %q)", b.SManufacturer)
			}
		})
	}
}

func TestResolveIdentityManufacturerDefault(t *testing.T) {
	b, _ := resolveIdentity(spHardware{SerialNumber: "S"}, ioPlatform{})
	if b.SManufacturer != "Apple Inc." {
		t.Errorf("default manufacturer = %q, want %q", b.SManufacturer, "Apple Inc.")
	}
	b2, _ := resolveIdentity(spHardware{SerialNumber: "S"}, ioPlatform{Manufacturer: "Apple Computer, Inc."})
	if b2.SManufacturer != "Apple Computer, Inc." {
		t.Errorf("ioreg manufacturer = %q", b2.SManufacturer)
	}
}

// --- ioreg ---

const ioregSample = `+-o J316sAP  <class IOPlatformExpertDevice, id 0x100000241, registered>
  {
    "IOPlatformSerialNumber" = "C02XYZ12345"
    "IOPlatformUUID" = "00006000-0011223344556677"
    "manufacturer" = <"Apple Inc.">
    "model" = <"MacBookPro18,3">
  }
`

func TestParseIOReg(t *testing.T) {
	p := parseIOReg(ioregSample)
	if p.Serial != "C02XYZ12345" {
		t.Errorf("serial = %q", p.Serial)
	}
	if p.UUID != "00006000-0011223344556677" {
		t.Errorf("uuid = %q", p.UUID)
	}
	if p.Manufacturer != "Apple Inc." {
		t.Errorf("manufacturer = %q", p.Manufacturer)
	}
	if p.Model != "MacBookPro18,3" {
		t.Errorf("model = %q", p.Model)
	}
}

// --- SPMemoryDataType ---

const spMemoryIntel = `{
  "SPMemoryDataType": [
    {
      "_name": "memory_overview",
      "_items": [
        {"_name": "BANK 0/ChannelA-DIMM0", "dimm_size": "8 GB", "dimm_type": "DDR4", "dimm_speed": "2667 MHz", "dimm_manufacturer": "0x802C", "dimm_serial_number": "0x00000001"},
        {"_name": "BANK 1/ChannelB-DIMM0", "dimm_size": "8 GB", "dimm_type": "DDR4", "dimm_speed": "2667 MHz"},
        {"_name": "BANK 2/Empty", "dimm_size": "Empty", "dimm_type": "Empty"}
      ]
    }
  ]
}`

func TestParseSPMemoryIntel(t *testing.T) {
	mems := parseSPMemory([]byte(spMemoryIntel))
	if len(mems) != 2 {
		t.Fatalf("got %d modules, want 2 (empty bank skipped): %+v", len(mems), mems)
	}
	if mems[0].Capacity != 8192 || mems[0].Type != "DDR4" || mems[0].Speed != "2667 MHz" {
		t.Errorf("module 0 = %+v", mems[0])
	}
	if mems[0].NumSlots != 1 || mems[1].NumSlots != 2 {
		t.Errorf("slot numbering = %d, %d", mems[0].NumSlots, mems[1].NumSlots)
	}
}

const spMemoryAppleSilicon = `{
  "SPMemoryDataType": [
    {"_name": "memory", "dimm_type": "LPDDR5", "SPMemoryDataType": "16 GB", "dimm_manufacturer": "Micron"}
  ]
}`

func TestParseSPMemoryAppleSilicon(t *testing.T) {
	mems := parseSPMemory([]byte(spMemoryAppleSilicon))
	if len(mems) != 1 {
		t.Fatalf("got %d modules, want 1 unified: %+v", len(mems), mems)
	}
	if mems[0].Capacity != 16384 || mems[0].Type != "LPDDR5" {
		t.Errorf("unified module = %+v", mems[0])
	}
}

// --- SPNVMeDataType ---

const spNVMe = `{
  "SPNVMeDataType": [
    {
      "_name": "Apple SSD Controller",
      "_items": [
        {"_name": "APPLE SSD AP0512", "bsd_name": "disk0", "size": "500.28 GB", "size_in_bytes": 500277790720, "device_model": "APPLE SSD AP0512", "device_serial": "0ABCDEF12345", "device_revision": "1234"}
      ]
    }
  ]
}`

func TestParseSPStorageNVMe(t *testing.T) {
	st := parseSPStorage([]byte(spNVMe), "SPNVMeDataType", "NVMe")
	if len(st) != 1 {
		t.Fatalf("got %d disks, want 1: %+v", len(st), st)
	}
	d := st[0]
	if d.Name != "/dev/disk0" || d.Type != "NVMe" {
		t.Errorf("disk name/type = %q/%q", d.Name, d.Type)
	}
	if d.DiskSize != 477102 { // 500277790720 / 1024 / 1024
		t.Errorf("disk size MB = %d", d.DiskSize)
	}
	if d.SerialNumber != "0ABCDEF12345" || d.Firmware != "1234" {
		t.Errorf("serial/firmware = %q/%q", d.SerialNumber, d.Firmware)
	}
}

// --- SPUSBDataType ---

const spUSB = `{
  "SPUSBDataType": [
    {
      "_name": "USB31Bus",
      "_items": [
        {"_name": "USB3.0 Hub", "vendor_id": "0x05e3  (Genesys Logic, Inc.)", "product_id": "0x0610", "_items": [
          {"_name": "USB Keyboard", "vendor_id": "0x05ac  (Apple Inc.)", "product_id": "0x024f", "serial_num": "KBD123", "manufacturer": "Apple Inc."}
        ]}
      ]
    }
  ]
}`

func TestParseSPUSB(t *testing.T) {
	dev := parseSPUSB([]byte(spUSB))
	if len(dev) != 1 {
		t.Fatalf("got %d devices, want 1 (hub skipped): %+v", len(dev), dev)
	}
	d := dev[0]
	if d.VendorID != "05ac" || d.ProductID != "024f" {
		t.Errorf("vid/pid = %q/%q", d.VendorID, d.ProductID)
	}
	if d.Name != "USB Keyboard" || d.Serial != "KBD123" {
		t.Errorf("name/serial = %q/%q", d.Name, d.Serial)
	}
	if d.Manufacturer != "Apple Inc." {
		t.Errorf("manufacturer = %q", d.Manufacturer)
	}
}

// --- SPApplicationsDataType ---

const spApps = `{
  "SPApplicationsDataType": [
    {"_name": "Safari", "version": "17.5", "obtained_from": "apple", "lastModified": "2024-05-01T00:00:00Z", "arch_kind": "arch_arm_i64"},
    {"_name": "Visual Studio Code", "version": "1.90.0", "obtained_from": "identified_developer"}
  ]
}`

func TestParseSPApplications(t *testing.T) {
	sw := parseSPApplications([]byte(spApps))
	if len(sw) != 2 {
		t.Fatalf("got %d apps, want 2: %+v", len(sw), sw)
	}
	if sw[0].Name != "Safari" || sw[0].Version != "17.5" || sw[0].Publisher != "apple" {
		t.Errorf("app 0 = %+v", sw[0])
	}
	if sw[0].From != "system_profiler" {
		t.Errorf("FROM = %q", sw[0].From)
	}
	if sw[1].Name != "Visual Studio Code" || sw[1].Version != "1.90.0" {
		t.Errorf("app 1 = %+v", sw[1])
	}
}

// --- route ---

func TestParseRouteGateway(t *testing.T) {
	const out = `   route to: default
destination: default
       mask: default
    gateway: 192.168.1.1
  interface: en0`
	if gw := parseRouteGateway(out); gw != "192.168.1.1" {
		t.Errorf("gateway = %q, want 192.168.1.1", gw)
	}
	if gw := parseRouteGateway("no gateway here"); gw != "" {
		t.Errorf("gateway = %q, want empty", gw)
	}
}

// --- memSizeMB ---

func TestMemSizeMB(t *testing.T) {
	cases := map[string]int{
		"16 GB": 16384, "8192 MB": 8192, "1 TB": 1048576, "": 0, "Empty": 0,
	}
	for in, want := range cases {
		if got := memSizeMB(in); got != want {
			t.Errorf("memSizeMB(%q) = %d, want %d", in, got, want)
		}
	}
}
