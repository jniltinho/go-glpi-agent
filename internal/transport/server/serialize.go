package server

import (
	"encoding/xml"

	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/version"
)

// Serialize converte um Inventory no XML <REQUEST> do protocolo
// OCS/FusionInventory, com QUERY=INVENTORY.
func Serialize(inv *inventory.Inventory) ([]byte, error) {
	req := BuildRequest(inv)
	body, err := xml.MarshalIndent(req, "", "  ")
	if err != nil {
		return nil, err
	}
	out := append([]byte(xml.Header), body...)
	return append(out, '\n'), nil
}

// BuildRequest monta a struct Request a partir do inventário (exposto para
// testes de compatibilidade).
func BuildRequest(inv *inventory.Inventory) Request {
	c := Content{VersionClient: version.UserAgent()}

	c.Hardware = buildHardware(inv)
	c.OperatingSystem = buildOS(inv)
	if b := buildBIOS(inv.BIOS); b != nil {
		c.BIOS = b
	}

	for _, cpu := range inv.CPUs {
		c.CPUs = append(c.CPUs, xmlCPU{
			Name: cpu.Name, Manufacturer: cpu.Manufacturer, Speed: cpu.Speed,
			Core: cpu.Core, Thread: cpu.Thread, Arch: cpu.Arch, CoreCount: cpu.CoreCount,
			ID: cpu.ID, Stepping: cpu.Stepping, FamilyNumber: cpu.FamilyNumber, Model: cpu.Model,
		})
	}
	for _, m := range inv.Memories {
		c.Memories = append(c.Memories, xmlMemory{
			Capacity: m.Capacity, Type: m.Type, Description: m.Description, Caption: m.Caption,
			Speed: m.Speed, NumSlots: m.NumSlots, SerialNumber: m.SerialNumber, Manufacturer: m.Manufacturer,
		})
	}
	for _, d := range inv.Drives {
		c.Drives = append(c.Drives, xmlDrive{
			Volumn: d.Volumn, Type: d.Type, FileSystem: d.FileSystem,
			Total: d.Total, Free: d.Free, Label: d.Label, Serial: d.Serial,
		})
	}
	for _, s := range inv.Storages {
		c.Storages = append(c.Storages, xmlStorage{
			Name: s.Name, Manufacturer: s.Manufacturer, Model: s.Model, Description: s.Description,
			Type: s.Type, DiskSize: s.DiskSize, SerialNumber: s.SerialNumber, Firmware: s.Firmware, WWN: s.WWN,
		})
	}
	// volumes LVM viram STORAGES tipo "lvm"
	for _, v := range inv.Volumes {
		c.Storages = append(c.Storages, xmlStorage{
			Name:        v.LVName,
			Description: v.VGName,
			Type:        "lvm",
			DiskSize:    v.Size,
		})
	}
	for _, n := range inv.Networks {
		c.Networks = append(c.Networks, xmlNetwork{
			Description: n.Description, Type: n.Type, Speed: n.Speed, MACAddr: n.MACAddr,
			Status: n.Status, VirtualDev: n.VirtualDev, IPAddress: n.IPAddress, IPMask: n.IPMask,
			IPSubnet: n.IPSubnet, IPGateway: n.IPGateway, IPAddress6: n.IPAddress6, MTU: n.MTU, Driver: n.Driver,
		})
	}
	for _, s := range inv.Softwares {
		c.Softwares = append(c.Softwares, xmlSoftware{
			Name: s.Name, Version: s.Version, Arch: s.Arch, Comments: s.Comments, FileSize: s.FileSize,
			From: s.From, InstallDate: s.InstallDate, Publisher: s.Publisher, Section: s.Section,
		})
	}
	for _, u := range inv.USBDevices {
		c.USBDevices = append(c.USBDevices, xmlUSB{
			VendorID: u.VendorID, ProductID: u.ProductID, Manufacturer: u.Manufacturer, Caption: u.Caption,
			Serial: u.Serial, Class: u.Class, SubClass: u.SubClass, Name: u.Name,
		})
	}
	for _, u := range inv.LocalUsers {
		c.LocalUsers = append(c.LocalUsers, xmlLUser{
			Login: u.Login, ID: u.ID, Name: u.Name, Home: u.Home, Shell: u.Shell,
		})
	}
	for _, g := range inv.LocalGroups {
		c.LocalGroups = append(c.LocalGroups, xmlLGroup{ID: g.ID, Name: g.Name, Member: g.Member})
	}
	for _, u := range inv.Users {
		c.Users = append(c.Users, xmlUser{Login: u.Login, Domain: u.Domain})
	}
	for _, p := range inv.Processes {
		c.Processes = append(c.Processes, xmlProcess{
			User: p.User, PID: p.PID, CPUUsage: p.CPUUsage, Mem: p.Mem,
			VirtualMemory: p.VirtualMemory, TTY: p.TTY, Started: p.Started, Cmd: p.Cmd,
		})
	}

	// tag de entidade vai em ACCOUNTINFO/TAG
	if inv.Tag != "" {
		c.AccountInfo = &xmlAccount{KeyName: "TAG", KeyValue: inv.Tag}
	}

	return Request{
		DeviceID: inv.DeviceID,
		Query:    "INVENTORY",
		Content:  c,
	}
}

func buildHardware(inv *inventory.Inventory) *xmlHardware {
	h := inv.Hardware
	return &xmlHardware{
		Name: h.Name, OSName: h.OSName, OSVersion: h.OSVersion, OSComments: h.OSComments,
		ArchName: h.ArchName, Memory: h.Memory, Swap: h.Swap, UUID: h.UUID, DNS: h.DNS,
		DefaultGateway: h.DefaultGateway, Workgroup: h.Workgroup, ChassisType: h.ChassisType,
		VMSystem: h.VMSystem, LastLoggedUser: h.LastLoggedUser, DateLastLoggedUser: h.DateLastLoggedUser,
	}
}

func buildOS(inv *inventory.Inventory) *xmlOS {
	o := inv.OperatingSystem
	x := &xmlOS{
		Name: o.Name, Version: o.Version, FullName: o.FullName, KernelName: o.KernelName,
		KernelVersion: o.KernelVersion, Arch: o.Arch, BootTime: o.BootTime, FQDN: o.FQDN,
		DNSDomain: o.DNSDomain, HostID: o.HostID, InstallDate: o.InstallDate,
	}
	if o.TimezoneName != "" || o.TimezoneUTCO != "" {
		x.Timezone = &xmlTimezone{Name: o.TimezoneName, Offset: o.TimezoneUTCO}
	}
	return x
}

func buildBIOS(b inventory.BIOS) *xmlBIOS {
	if b == (inventory.BIOS{}) {
		return nil
	}
	return &xmlBIOS{
		SManufacturer: b.SManufacturer, SModel: b.SModel, SSN: b.SSN, BManufacturer: b.BManufacturer,
		BVersion: b.BVersion, BDate: b.BDate, AssetTag: b.AssetTag, MManufacturer: b.MManufacturer,
		MModel: b.MModel, MSN: b.MSN,
	}
}
