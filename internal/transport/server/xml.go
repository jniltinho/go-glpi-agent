// Package server implementa o transporte para servidor GLPI: serialização XML
// no formato OCS/FusionInventory, fluxo PROLOG e cliente HTTP.
package server

import "encoding/xml"

// Request é o envelope <REQUEST> enviado ao GLPI.
type Request struct {
	XMLName  xml.Name `xml:"REQUEST"`
	DeviceID string   `xml:"DEVICEID"`
	Query    string   `xml:"QUERY"`
	Content  Content  `xml:"CONTENT"`
}

// Content é a seção <CONTENT> com todas as categorias de inventário.
type Content struct {
	Hardware        *xmlHardware  `xml:"HARDWARE,omitempty"`
	BIOS            *xmlBIOS      `xml:"BIOS,omitempty"`
	OperatingSystem *xmlOS        `xml:"OPERATINGSYSTEM,omitempty"`
	CPUs            []xmlCPU      `xml:"CPUS,omitempty"`
	Memories        []xmlMemory   `xml:"MEMORIES,omitempty"`
	Drives          []xmlDrive    `xml:"DRIVES,omitempty"`
	Storages        []xmlStorage  `xml:"STORAGES,omitempty"`
	Networks        []xmlNetwork  `xml:"NETWORKS,omitempty"`
	Softwares       []xmlSoftware `xml:"SOFTWARES,omitempty"`
	USBDevices      []xmlUSB      `xml:"USBDEVICES,omitempty"`
	LocalUsers      []xmlLUser    `xml:"LOCAL_USERS,omitempty"`
	LocalGroups     []xmlLGroup   `xml:"LOCAL_GROUPS,omitempty"`
	Users           []xmlUser     `xml:"USERS,omitempty"`
	Processes       []xmlProcess  `xml:"PROCESSES,omitempty"`
	AccountInfo     *xmlAccount   `xml:"ACCOUNTINFO,omitempty"`
	VersionClient   string        `xml:"VERSIONCLIENT,omitempty"`
}

type xmlHardware struct {
	Name               string `xml:"NAME,omitempty"`
	OSName             string `xml:"OSNAME,omitempty"`
	OSVersion          string `xml:"OSVERSION,omitempty"`
	OSComments         string `xml:"OSCOMMENTS,omitempty"`
	ArchName           string `xml:"ARCHNAME,omitempty"`
	Memory             int    `xml:"MEMORY,omitempty"`
	Swap               int    `xml:"SWAP,omitempty"`
	UUID               string `xml:"UUID,omitempty"`
	DNS                string `xml:"DNS,omitempty"`
	DefaultGateway     string `xml:"DEFAULTGATEWAY,omitempty"`
	Workgroup          string `xml:"WORKGROUP,omitempty"`
	ChassisType        string `xml:"CHASSIS_TYPE,omitempty"`
	VMSystem           string `xml:"VMSYSTEM,omitempty"`
	LastLoggedUser     string `xml:"LASTLOGGEDUSER,omitempty"`
	DateLastLoggedUser string `xml:"DATELASTLOGGEDUSER,omitempty"`
}

type xmlBIOS struct {
	SManufacturer string `xml:"SMANUFACTURER,omitempty"`
	SModel        string `xml:"SMODEL,omitempty"`
	SSN           string `xml:"SSN,omitempty"`
	BManufacturer string `xml:"BMANUFACTURER,omitempty"`
	BVersion      string `xml:"BVERSION,omitempty"`
	BDate         string `xml:"BDATE,omitempty"`
	AssetTag      string `xml:"ASSETTAG,omitempty"`
	MManufacturer string `xml:"MMANUFACTURER,omitempty"`
	MModel        string `xml:"MMODEL,omitempty"`
	MSN           string `xml:"MSN,omitempty"`
}

type xmlOS struct {
	Name          string       `xml:"NAME,omitempty"`
	Version       string       `xml:"VERSION,omitempty"`
	FullName      string       `xml:"FULL_NAME,omitempty"`
	KernelName    string       `xml:"KERNEL_NAME,omitempty"`
	KernelVersion string       `xml:"KERNEL_VERSION,omitempty"`
	Arch          string       `xml:"ARCH,omitempty"`
	BootTime      string       `xml:"BOOT_TIME,omitempty"`
	FQDN          string       `xml:"FQDN,omitempty"`
	DNSDomain     string       `xml:"DNS_DOMAIN,omitempty"`
	HostID        string       `xml:"HOSTID,omitempty"`
	InstallDate   string       `xml:"INSTALL_DATE,omitempty"`
	Timezone      *xmlTimezone `xml:"TIMEZONE,omitempty"`
}

type xmlTimezone struct {
	Name   string `xml:"NAME,omitempty"`
	Offset string `xml:"OFFSET,omitempty"`
}

type xmlCPU struct {
	Name         string `xml:"NAME,omitempty"`
	Manufacturer string `xml:"MANUFACTURER,omitempty"`
	Speed        int    `xml:"SPEED,omitempty"`
	Core         int    `xml:"CORE,omitempty"`
	Thread       int    `xml:"THREAD,omitempty"`
	Arch         string `xml:"ARCH,omitempty"`
	CoreCount    int    `xml:"CORECOUNT,omitempty"`
	ID           string `xml:"ID,omitempty"`
	Stepping     string `xml:"STEPPING,omitempty"`
	FamilyNumber string `xml:"FAMILYNUMBER,omitempty"`
	Model        string `xml:"MODEL,omitempty"`
}

type xmlMemory struct {
	Capacity     int    `xml:"CAPACITY,omitempty"`
	Type         string `xml:"TYPE,omitempty"`
	Description  string `xml:"DESCRIPTION,omitempty"`
	Caption      string `xml:"CAPTION,omitempty"`
	Speed        string `xml:"SPEED,omitempty"`
	NumSlots     int    `xml:"NUMSLOTS,omitempty"`
	SerialNumber string `xml:"SERIALNUMBER,omitempty"`
	Manufacturer string `xml:"MANUFACTURER,omitempty"`
}

type xmlDrive struct {
	Volumn     string `xml:"VOLUMN,omitempty"`
	Type       string `xml:"TYPE,omitempty"`
	FileSystem string `xml:"FILESYSTEM,omitempty"`
	Total      int    `xml:"TOTAL,omitempty"`
	Free       int    `xml:"FREE,omitempty"`
	Label      string `xml:"LABEL,omitempty"`
	Serial     string `xml:"SERIAL,omitempty"`
}

type xmlStorage struct {
	Name         string `xml:"NAME,omitempty"`
	Manufacturer string `xml:"MANUFACTURER,omitempty"`
	Model        string `xml:"MODEL,omitempty"`
	Description  string `xml:"DESCRIPTION,omitempty"`
	Type         string `xml:"TYPE,omitempty"`
	DiskSize     int    `xml:"DISKSIZE,omitempty"`
	SerialNumber string `xml:"SERIALNUMBER,omitempty"`
	Firmware     string `xml:"FIRMWARE,omitempty"`
	WWN          string `xml:"WWN,omitempty"`
}

type xmlNetwork struct {
	Description string `xml:"DESCRIPTION,omitempty"`
	Type        string `xml:"TYPE,omitempty"`
	Speed       string `xml:"SPEED,omitempty"`
	MACAddr     string `xml:"MACADDR,omitempty"`
	Status      string `xml:"STATUS,omitempty"`
	VirtualDev  string `xml:"VIRTUALDEV,omitempty"`
	IPAddress   string `xml:"IPADDRESS,omitempty"`
	IPMask      string `xml:"IPMASK,omitempty"`
	IPSubnet    string `xml:"IPSUBNET,omitempty"`
	IPGateway   string `xml:"IPGATEWAY,omitempty"`
	IPAddress6  string `xml:"IPADDRESS6,omitempty"`
	MTU         string `xml:"MTU,omitempty"`
	Driver      string `xml:"DRIVER,omitempty"`
}

type xmlSoftware struct {
	Name        string `xml:"NAME,omitempty"`
	Version     string `xml:"VERSION,omitempty"`
	Arch        string `xml:"ARCH,omitempty"`
	Comments    string `xml:"COMMENTS,omitempty"`
	FileSize    int64  `xml:"FILESIZE,omitempty"`
	From        string `xml:"FROM,omitempty"`
	InstallDate string `xml:"INSTALLDATE,omitempty"`
	Publisher   string `xml:"PUBLISHER,omitempty"`
	Section     string `xml:"SECTION,omitempty"`
}

type xmlUSB struct {
	VendorID     string `xml:"VENDORID,omitempty"`
	ProductID    string `xml:"PRODUCTID,omitempty"`
	Manufacturer string `xml:"MANUFACTURER,omitempty"`
	Caption      string `xml:"CAPTION,omitempty"`
	Serial       string `xml:"SERIAL,omitempty"`
	Class        string `xml:"CLASS,omitempty"`
	SubClass     string `xml:"SUBCLASS,omitempty"`
	Name         string `xml:"NAME,omitempty"`
}

type xmlLUser struct {
	Login string `xml:"LOGIN,omitempty"`
	ID    string `xml:"ID,omitempty"`
	Name  string `xml:"NAME,omitempty"`
	Home  string `xml:"HOME,omitempty"`
	Shell string `xml:"SHELL,omitempty"`
}

type xmlLGroup struct {
	ID     string   `xml:"ID,omitempty"`
	Name   string   `xml:"NAME,omitempty"`
	Member []string `xml:"MEMBER,omitempty"`
}

type xmlUser struct {
	Login  string `xml:"LOGIN,omitempty"`
	Domain string `xml:"DOMAIN,omitempty"`
}

type xmlProcess struct {
	User          string  `xml:"USER,omitempty"`
	PID           int32   `xml:"PID,omitempty"`
	CPUUsage      float64 `xml:"CPUUSAGE,omitempty"`
	Mem           float32 `xml:"MEM,omitempty"`
	VirtualMemory uint64  `xml:"VIRTUALMEMORY,omitempty"`
	TTY           string  `xml:"TTY,omitempty"`
	Started       string  `xml:"STARTED,omitempty"`
	Cmd           string  `xml:"CMD,omitempty"`
}

type xmlAccount struct {
	KeyName  string `xml:"KEYNAME,omitempty"`
	KeyValue string `xml:"KEYVALUE,omitempty"`
}
