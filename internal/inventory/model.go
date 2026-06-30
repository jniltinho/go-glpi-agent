// Package inventory defines the inventory data model and its serialization
// to the OCS/FusionInventory XML format accepted by the GLPI server.
package inventory

import "sync"

// Inventory accumulates the collected data. It is safe for concurrent writes:
// each collector runs in its own goroutine and uses the Set*/Add* methods that
// guard access with a mutex.
type Inventory struct {
	mu sync.Mutex

	DeviceID string
	AgentID  string // UUID sent in the GLPI-Agent-ID header (native protocol)
	Tag      string

	Hardware        Hardware
	BIOS            BIOS
	OperatingSystem OperatingSystem
	CPUs            []CPU
	Memories        []Memory
	Drives          []Drive   // mounted filesystems (DRIVES)
	Storages        []Storage // physical disks (STORAGES)
	Networks        []Network
	Softwares       []Software
	USBDevices      []USBDevice
	LocalUsers      []LocalUser
	LocalGroups     []LocalGroup
	Users           []User
	Processes       []Process
	Volumes         []Volume // LVM logical volumes
}

// New creates an empty inventory.
func New(deviceID string) *Inventory {
	return &Inventory{DeviceID: deviceID}
}

// Hardware corresponds to the <HARDWARE> section of the XML.
type Hardware struct {
	Name               string
	OSName             string
	OSVersion          string
	OSComments         string
	ArchName           string
	Memory             int // MB
	Swap               int // MB
	UUID               string
	DNS                string
	DefaultGateway     string
	Workgroup          string
	ChassisType        string
	VMSystem           string
	LastLoggedUser     string
	DateLastLoggedUser string
}

// BIOS corresponds to the <BIOS> section.
type BIOS struct {
	SManufacturer string // system manufacturer
	SModel        string // system model
	SSN           string // system serial
	BManufacturer string // BIOS manufacturer
	BVersion      string
	BDate         string
	AssetTag      string
	MManufacturer string // motherboard manufacturer
	MModel        string
	MSN           string
}

// OperatingSystem corresponds to the <OPERATINGSYSTEM> section.
type OperatingSystem struct {
	Name          string
	Version       string
	FullName      string
	KernelName    string
	KernelVersion string
	Arch          string
	BootTime      string
	FQDN          string
	DNSDomain     string
	HostID        string
	InstallDate   string
	TimezoneName  string
	TimezoneUTCO  string // offset, e.g. "+0200"
}

// CPU corresponds to each <CPUS>.
type CPU struct {
	Name         string
	Manufacturer string
	Speed        int // MHz
	Core         int // physical cores
	Thread       int // threads per core
	Arch         string
	CoreCount    int // total logical cores
	ID           string
	Stepping     string
	FamilyNumber string
	Model        string
}

// Memory corresponds to each <MEMORIES> (physical slot).
type Memory struct {
	Capacity     int // MB
	Type         string
	Description  string
	Caption      string
	Speed        string
	NumSlots     int
	SerialNumber string
	Manufacturer string
}

// Drive corresponds to each <DRIVES> (mounted filesystem).
type Drive struct {
	Volumn     string // device (e.g. /dev/sda1)
	Type       string // mount point
	FileSystem string
	Total      int // MB
	Free       int // MB
	Label      string
	Serial     string
}

// Storage corresponds to each <STORAGES> (physical disk).
type Storage struct {
	Name         string
	Manufacturer string
	Model        string
	Description  string
	Type         string // disk, removable...
	DiskSize     int    // MB
	SerialNumber string
	Firmware     string
	WWN          string
}

// Network corresponds to each <NETWORKS> (interface).
type Network struct {
	Description string
	Type        string // ethernet, wifi, loopback...
	Speed       string
	MACAddr     string
	Status      string // Up / Down
	VirtualDev  string // 1 if virtual
	IPAddress   string
	IPMask      string
	IPSubnet    string
	IPGateway   string
	IPAddress6  string
	MTU         string
	Driver      string
}

// Software corresponds to each <SOFTWARES>.
type Software struct {
	Name        string
	Version     string
	Arch        string
	Comments    string
	FileSize    int64
	From        string // dpkg, rpm, pacman
	InstallDate string
	Publisher   string
	Section     string
}

// USBDevice corresponds to each <USBDEVICES>.
type USBDevice struct {
	VendorID     string
	ProductID    string
	Manufacturer string
	Caption      string
	Serial       string
	Class        string
	SubClass     string
	Name         string
}

// LocalUser corresponds to each <LOCAL_USERS>.
type LocalUser struct {
	Login string
	ID    string
	Name  string
	Home  string
	Shell string
}

// LocalGroup corresponds to each <LOCAL_GROUPS>.
type LocalGroup struct {
	ID     string
	Name   string
	Member []string
}

// User corresponds to each <USERS> (logged-in session).
type User struct {
	Login  string
	Domain string
}

// Process corresponds to each <PROCESSES>.
type Process struct {
	User          string
	PID           int32
	CPUUsage      float64
	Mem           float32
	VirtualMemory uint64
	TTY           string
	Started       string
	Cmd           string
}

// Volume corresponds to an LVM logical volume (mapped to STORAGES in the XML).
type Volume struct {
	LVName   string
	VGName   string
	Size     int // MB
	Attr     string
	LVUUID   string
	SegStart string
	SegCount string
}

// ---- Thread-safe setters/adders ----

// SetHardware mutates the Hardware section under the lock; safe for concurrent use.
func (inv *Inventory) SetHardware(fn func(h *Hardware)) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	fn(&inv.Hardware)
}

// SetBIOS mutates the BIOS section under the lock; safe for concurrent use.
func (inv *Inventory) SetBIOS(fn func(b *BIOS)) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	fn(&inv.BIOS)
}

// SetOperatingSystem mutates the OperatingSystem section under the lock; safe for concurrent use.
func (inv *Inventory) SetOperatingSystem(fn func(o *OperatingSystem)) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	fn(&inv.OperatingSystem)
}

// AddCPU appends c to the inventory; safe for concurrent use.
func (inv *Inventory) AddCPU(c CPU) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.CPUs = append(inv.CPUs, c)
}

// AddMemory appends m to the inventory; safe for concurrent use.
func (inv *Inventory) AddMemory(m Memory) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Memories = append(inv.Memories, m)
}

// AddDrive appends d to the inventory; safe for concurrent use.
func (inv *Inventory) AddDrive(d Drive) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Drives = append(inv.Drives, d)
}

// AddStorage appends s to the inventory; safe for concurrent use.
func (inv *Inventory) AddStorage(s Storage) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Storages = append(inv.Storages, s)
}

// AddNetwork appends n to the inventory; safe for concurrent use.
func (inv *Inventory) AddNetwork(n Network) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Networks = append(inv.Networks, n)
}

// AddSoftware appends s to the inventory; safe for concurrent use.
func (inv *Inventory) AddSoftware(s Software) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Softwares = append(inv.Softwares, s)
}

// AddUSBDevice appends u to the inventory; safe for concurrent use.
func (inv *Inventory) AddUSBDevice(u USBDevice) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.USBDevices = append(inv.USBDevices, u)
}

// AddLocalUser appends u to the inventory; safe for concurrent use.
func (inv *Inventory) AddLocalUser(u LocalUser) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.LocalUsers = append(inv.LocalUsers, u)
}

// AddLocalGroup appends g to the inventory; safe for concurrent use.
func (inv *Inventory) AddLocalGroup(g LocalGroup) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.LocalGroups = append(inv.LocalGroups, g)
}

// AddUser appends u to the inventory; safe for concurrent use.
func (inv *Inventory) AddUser(u User) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Users = append(inv.Users, u)
}

// AddProcess appends p to the inventory; safe for concurrent use.
func (inv *Inventory) AddProcess(p Process) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Processes = append(inv.Processes, p)
}

// AddVolume appends v to the inventory; safe for concurrent use.
func (inv *Inventory) AddVolume(v Volume) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Volumes = append(inv.Volumes, v)
}
