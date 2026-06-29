// Package inventory define o modelo de dados do inventário e sua serialização
// para o formato XML OCS/FusionInventory aceito pelo servidor GLPI.
package inventory

import "sync"

// Inventory acumula os dados coletados. É seguro para escrita concorrente:
// cada coletor roda em sua própria goroutine e usa os métodos Set*/Add* que
// protegem o acesso com mutex.
type Inventory struct {
	mu sync.Mutex

	DeviceID string
	Tag      string

	Hardware        Hardware
	BIOS            BIOS
	OperatingSystem OperatingSystem
	CPUs            []CPU
	Memories        []Memory
	Drives          []Drive   // sistemas de arquivos montados (DRIVES)
	Storages        []Storage // discos físicos (STORAGES)
	Networks        []Network
	Softwares       []Software
	USBDevices      []USBDevice
	LocalUsers      []LocalUser
	LocalGroups     []LocalGroup
	Users           []User
	Processes       []Process
	Volumes         []Volume // LVM logical volumes
}

// New cria um inventário vazio.
func New(deviceID string) *Inventory {
	return &Inventory{DeviceID: deviceID}
}

// Hardware corresponde à seção <HARDWARE> do XML.
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

// BIOS corresponde à seção <BIOS>.
type BIOS struct {
	SManufacturer string // fabricante do sistema
	SModel        string // modelo do sistema
	SSN           string // serial do sistema
	BManufacturer string // fabricante do BIOS
	BVersion      string
	BDate         string
	AssetTag      string
	MManufacturer string // fabricante da placa-mãe
	MModel        string
	MSN           string
}

// OperatingSystem corresponde à seção <OPERATINGSYSTEM>.
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
	TimezoneUTCO  string // offset, ex: "+0200"
}

// CPU corresponde a cada <CPUS>.
type CPU struct {
	Name         string
	Manufacturer string
	Speed        int // MHz
	Core         int // núcleos físicos
	Thread       int // threads por core
	Arch         string
	CoreCount    int // total de cores lógicos
	ID           string
	Stepping     string
	FamilyNumber string
	Model        string
}

// Memory corresponde a cada <MEMORIES> (slot físico).
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

// Drive corresponde a cada <DRIVES> (sistema de arquivos montado).
type Drive struct {
	Volumn     string // dispositivo (ex: /dev/sda1)
	Type       string // ponto de montagem
	FileSystem string
	Total      int // MB
	Free       int // MB
	Label      string
	Serial     string
}

// Storage corresponde a cada <STORAGES> (disco físico).
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

// Network corresponde a cada <NETWORKS> (interface).
type Network struct {
	Description string
	Type        string // ethernet, wifi, loopback...
	Speed       string
	MACAddr     string
	Status      string // Up / Down
	VirtualDev  string // 1 se virtual
	IPAddress   string
	IPMask      string
	IPSubnet    string
	IPGateway   string
	IPAddress6  string
	MTU         string
	Driver      string
}

// Software corresponde a cada <SOFTWARES>.
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

// USBDevice corresponde a cada <USBDEVICES>.
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

// LocalUser corresponde a cada <LOCAL_USERS>.
type LocalUser struct {
	Login string
	ID    string
	Name  string
	Home  string
	Shell string
}

// LocalGroup corresponde a cada <LOCAL_GROUPS>.
type LocalGroup struct {
	ID     string
	Name   string
	Member []string
}

// User corresponde a cada <USERS> (sessão logada).
type User struct {
	Login  string
	Domain string
}

// Process corresponde a cada <PROCESSES>.
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

// Volume corresponde a um volume lógico LVM (mapeado em STORAGES no XML).
type Volume struct {
	LVName   string
	VGName   string
	Size     int // MB
	Attr     string
	LVUUID   string
	SegStart string
	SegCount string
}

// ---- Setters/Adders thread-safe ----

func (inv *Inventory) SetHardware(fn func(h *Hardware)) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	fn(&inv.Hardware)
}

func (inv *Inventory) SetBIOS(fn func(b *BIOS)) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	fn(&inv.BIOS)
}

func (inv *Inventory) SetOperatingSystem(fn func(o *OperatingSystem)) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	fn(&inv.OperatingSystem)
}

func (inv *Inventory) AddCPU(c CPU) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.CPUs = append(inv.CPUs, c)
}

func (inv *Inventory) AddMemory(m Memory) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Memories = append(inv.Memories, m)
}

func (inv *Inventory) AddDrive(d Drive) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Drives = append(inv.Drives, d)
}

func (inv *Inventory) AddStorage(s Storage) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Storages = append(inv.Storages, s)
}

func (inv *Inventory) AddNetwork(n Network) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Networks = append(inv.Networks, n)
}

func (inv *Inventory) AddSoftware(s Software) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Softwares = append(inv.Softwares, s)
}

func (inv *Inventory) AddUSBDevice(u USBDevice) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.USBDevices = append(inv.USBDevices, u)
}

func (inv *Inventory) AddLocalUser(u LocalUser) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.LocalUsers = append(inv.LocalUsers, u)
}

func (inv *Inventory) AddLocalGroup(g LocalGroup) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.LocalGroups = append(inv.LocalGroups, g)
}

func (inv *Inventory) AddUser(u User) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Users = append(inv.Users, u)
}

func (inv *Inventory) AddProcess(p Process) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Processes = append(inv.Processes, p)
}

func (inv *Inventory) AddVolume(v Volume) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	inv.Volumes = append(inv.Volumes, v)
}
