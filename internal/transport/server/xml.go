// Package server implements the transport for a GLPI server: XML serialization
// in the OCS/FusionInventory format, the PROLOG flow and an HTTP client.
package server

import "encoding/xml"

// Request is the <REQUEST> envelope sent to GLPI.
type Request struct {
	XMLName  xml.Name `xml:"REQUEST"`
	DeviceID string   `xml:"DEVICEID"`
	Query    string   `xml:"QUERY"`
	Content  Content  `xml:"CONTENT"`
}

// Content is the <CONTENT> section with all inventory categories. The same
// structs feed both the XML (uppercase tags) and GLPI's native JSON (lowercase
// tags). AccountInfo is left out of the JSON (json:"-") because the native
// protocol uses an `accountinfo` array — assembled in json.go.
type Content struct {
	Hardware        *xmlHardware  `xml:"HARDWARE,omitempty" json:"hardware,omitempty"`
	BIOS            *xmlBIOS      `xml:"BIOS,omitempty" json:"bios,omitempty"`
	OperatingSystem *xmlOS        `xml:"OPERATINGSYSTEM,omitempty" json:"operatingsystem,omitempty"`
	CPUs            []xmlCPU      `xml:"CPUS,omitempty" json:"cpus,omitempty"`
	Memories        []xmlMemory   `xml:"MEMORIES,omitempty" json:"memories,omitempty"`
	Drives          []xmlDrive    `xml:"DRIVES,omitempty" json:"drives,omitempty"`
	Storages        []xmlStorage  `xml:"STORAGES,omitempty" json:"storages,omitempty"`
	Networks        []xmlNetwork  `xml:"NETWORKS,omitempty" json:"networks,omitempty"`
	Softwares       []xmlSoftware `xml:"SOFTWARES,omitempty" json:"softwares,omitempty"`
	USBDevices      []xmlUSB      `xml:"USBDEVICES,omitempty" json:"usbdevices,omitempty"`
	LocalUsers      []xmlLUser    `xml:"LOCAL_USERS,omitempty" json:"local_users,omitempty"`
	LocalGroups     []xmlLGroup   `xml:"LOCAL_GROUPS,omitempty" json:"local_groups,omitempty"`
	Users           []xmlUser     `xml:"USERS,omitempty" json:"users,omitempty"`
	Processes       []xmlProcess  `xml:"PROCESSES,omitempty" json:"processes,omitempty"`
	AccountInfo     *xmlAccount   `xml:"ACCOUNTINFO,omitempty" json:"-"`
	VersionClient   string        `xml:"VERSIONCLIENT,omitempty" json:"versionclient,omitempty"`
}

type xmlHardware struct {
	Name               string `xml:"NAME,omitempty" json:"name,omitempty"`
	OSName             string `xml:"OSNAME,omitempty" json:"osname,omitempty"`
	OSVersion          string `xml:"OSVERSION,omitempty" json:"osversion,omitempty"`
	OSComments         string `xml:"OSCOMMENTS,omitempty" json:"oscomments,omitempty"`
	ArchName           string `xml:"ARCHNAME,omitempty" json:"archname,omitempty"`
	Memory             int    `xml:"MEMORY,omitempty" json:"memory,omitempty"`
	Swap               int    `xml:"SWAP,omitempty" json:"swap,omitempty"`
	UUID               string `xml:"UUID,omitempty" json:"uuid,omitempty"`
	DNS                string `xml:"DNS,omitempty" json:"dns,omitempty"`
	DefaultGateway     string `xml:"DEFAULTGATEWAY,omitempty" json:"defaultgateway,omitempty"`
	Workgroup          string `xml:"WORKGROUP,omitempty" json:"workgroup,omitempty"`
	ChassisType        string `xml:"CHASSIS_TYPE,omitempty" json:"chassis_type,omitempty"`
	VMSystem           string `xml:"VMSYSTEM,omitempty" json:"vmsystem,omitempty"`
	LastLoggedUser     string `xml:"LASTLOGGEDUSER,omitempty" json:"lastloggeduser,omitempty"`
	DateLastLoggedUser string `xml:"DATELASTLOGGEDUSER,omitempty" json:"datelastloggeduser,omitempty"`
}

type xmlBIOS struct {
	SManufacturer string `xml:"SMANUFACTURER,omitempty" json:"smanufacturer,omitempty"`
	SModel        string `xml:"SMODEL,omitempty" json:"smodel,omitempty"`
	SSN           string `xml:"SSN,omitempty" json:"ssn,omitempty"`
	BManufacturer string `xml:"BMANUFACTURER,omitempty" json:"bmanufacturer,omitempty"`
	BVersion      string `xml:"BVERSION,omitempty" json:"bversion,omitempty"`
	BDate         string `xml:"BDATE,omitempty" json:"bdate,omitempty"`
	AssetTag      string `xml:"ASSETTAG,omitempty" json:"assettag,omitempty"`
	MManufacturer string `xml:"MMANUFACTURER,omitempty" json:"mmanufacturer,omitempty"`
	MModel        string `xml:"MMODEL,omitempty" json:"mmodel,omitempty"`
	MSN           string `xml:"MSN,omitempty" json:"msn,omitempty"`
}

type xmlOS struct {
	Name          string       `xml:"NAME,omitempty" json:"name,omitempty"`
	Version       string       `xml:"VERSION,omitempty" json:"version,omitempty"`
	FullName      string       `xml:"FULL_NAME,omitempty" json:"full_name,omitempty"`
	KernelName    string       `xml:"KERNEL_NAME,omitempty" json:"kernel_name,omitempty"`
	KernelVersion string       `xml:"KERNEL_VERSION,omitempty" json:"kernel_version,omitempty"`
	Arch          string       `xml:"ARCH,omitempty" json:"arch,omitempty"`
	BootTime      string       `xml:"BOOT_TIME,omitempty" json:"boot_time,omitempty"`
	FQDN          string       `xml:"FQDN,omitempty" json:"fqdn,omitempty"`
	DNSDomain     string       `xml:"DNS_DOMAIN,omitempty" json:"dns_domain,omitempty"`
	HostID        string       `xml:"HOSTID,omitempty" json:"hostid,omitempty"`
	InstallDate   string       `xml:"INSTALL_DATE,omitempty" json:"install_date,omitempty"`
	Timezone      *xmlTimezone `xml:"TIMEZONE,omitempty" json:"timezone,omitempty"`
}

type xmlTimezone struct {
	Name   string `xml:"NAME,omitempty" json:"name,omitempty"`
	Offset string `xml:"OFFSET,omitempty" json:"offset,omitempty"`
}

type xmlCPU struct {
	Name         string `xml:"NAME,omitempty" json:"name,omitempty"`
	Manufacturer string `xml:"MANUFACTURER,omitempty" json:"manufacturer,omitempty"`
	Speed        int    `xml:"SPEED,omitempty" json:"speed,omitempty"`
	Core         int    `xml:"CORE,omitempty" json:"core,omitempty"`
	Thread       int    `xml:"THREAD,omitempty" json:"thread,omitempty"`
	Arch         string `xml:"ARCH,omitempty" json:"arch,omitempty"`
	CoreCount    int    `xml:"CORECOUNT,omitempty" json:"corecount,omitempty"`
	ID           string `xml:"ID,omitempty" json:"id,omitempty"`
	Stepping     string `xml:"STEPPING,omitempty" json:"stepping,omitempty"`
	FamilyNumber string `xml:"FAMILYNUMBER,omitempty" json:"familynumber,omitempty"`
	Model        string `xml:"MODEL,omitempty" json:"model,omitempty"`
}

type xmlMemory struct {
	Capacity     int    `xml:"CAPACITY,omitempty" json:"capacity,omitempty"`
	Type         string `xml:"TYPE,omitempty" json:"type,omitempty"`
	Description  string `xml:"DESCRIPTION,omitempty" json:"description,omitempty"`
	Caption      string `xml:"CAPTION,omitempty" json:"caption,omitempty"`
	Speed        string `xml:"SPEED,omitempty" json:"speed,omitempty"`
	NumSlots     int    `xml:"NUMSLOTS,omitempty" json:"numslots,omitempty"`
	SerialNumber string `xml:"SERIALNUMBER,omitempty" json:"serialnumber,omitempty"`
	Manufacturer string `xml:"MANUFACTURER,omitempty" json:"manufacturer,omitempty"`
}

type xmlDrive struct {
	Volumn     string `xml:"VOLUMN,omitempty" json:"volumn,omitempty"`
	Type       string `xml:"TYPE,omitempty" json:"type,omitempty"`
	FileSystem string `xml:"FILESYSTEM,omitempty" json:"filesystem,omitempty"`
	Total      int    `xml:"TOTAL,omitempty" json:"total,omitempty"`
	Free       int    `xml:"FREE,omitempty" json:"free,omitempty"`
	Label      string `xml:"LABEL,omitempty" json:"label,omitempty"`
	Serial     string `xml:"SERIAL,omitempty" json:"serial,omitempty"`
}

type xmlStorage struct {
	Name         string `xml:"NAME,omitempty" json:"name,omitempty"`
	Manufacturer string `xml:"MANUFACTURER,omitempty" json:"manufacturer,omitempty"`
	Model        string `xml:"MODEL,omitempty" json:"model,omitempty"`
	Description  string `xml:"DESCRIPTION,omitempty" json:"description,omitempty"`
	Type         string `xml:"TYPE,omitempty" json:"type,omitempty"`
	DiskSize     int    `xml:"DISKSIZE,omitempty" json:"disksize,omitempty"`
	SerialNumber string `xml:"SERIALNUMBER,omitempty" json:"serialnumber,omitempty"`
	Firmware     string `xml:"FIRMWARE,omitempty" json:"firmware,omitempty"`
	WWN          string `xml:"WWN,omitempty" json:"wwn,omitempty"`
}

type xmlNetwork struct {
	Description string `xml:"DESCRIPTION,omitempty" json:"description,omitempty"`
	Type        string `xml:"TYPE,omitempty" json:"type,omitempty"`
	Speed       string `xml:"SPEED,omitempty" json:"speed,omitempty"`
	MACAddr     string `xml:"MACADDR,omitempty" json:"macaddr,omitempty"`
	Status      string `xml:"STATUS,omitempty" json:"status,omitempty"`
	VirtualDev  string `xml:"VIRTUALDEV,omitempty" json:"virtualdev,omitempty"`
	IPAddress   string `xml:"IPADDRESS,omitempty" json:"ipaddress,omitempty"`
	IPMask      string `xml:"IPMASK,omitempty" json:"ipmask,omitempty"`
	IPSubnet    string `xml:"IPSUBNET,omitempty" json:"ipsubnet,omitempty"`
	IPGateway   string `xml:"IPGATEWAY,omitempty" json:"ipgateway,omitempty"`
	IPAddress6  string `xml:"IPADDRESS6,omitempty" json:"ipaddress6,omitempty"`
	MTU         string `xml:"MTU,omitempty" json:"mtu,omitempty"`
	Driver      string `xml:"DRIVER,omitempty" json:"driver,omitempty"`
}

type xmlSoftware struct {
	Name        string `xml:"NAME,omitempty" json:"name,omitempty"`
	Version     string `xml:"VERSION,omitempty" json:"version,omitempty"`
	Arch        string `xml:"ARCH,omitempty" json:"arch,omitempty"`
	Comments    string `xml:"COMMENTS,omitempty" json:"comments,omitempty"`
	FileSize    int64  `xml:"FILESIZE,omitempty" json:"filesize,omitempty"`
	From        string `xml:"FROM,omitempty" json:"from,omitempty"`
	InstallDate string `xml:"INSTALLDATE,omitempty" json:"installdate,omitempty"`
	Publisher   string `xml:"PUBLISHER,omitempty" json:"publisher,omitempty"`
	Section     string `xml:"SECTION,omitempty" json:"section,omitempty"`
}

type xmlUSB struct {
	VendorID     string `xml:"VENDORID,omitempty" json:"vendorid,omitempty"`
	ProductID    string `xml:"PRODUCTID,omitempty" json:"productid,omitempty"`
	Manufacturer string `xml:"MANUFACTURER,omitempty" json:"manufacturer,omitempty"`
	Caption      string `xml:"CAPTION,omitempty" json:"caption,omitempty"`
	Serial       string `xml:"SERIAL,omitempty" json:"serial,omitempty"`
	Class        string `xml:"CLASS,omitempty" json:"class,omitempty"`
	SubClass     string `xml:"SUBCLASS,omitempty" json:"subclass,omitempty"`
	Name         string `xml:"NAME,omitempty" json:"name,omitempty"`
}

type xmlLUser struct {
	Login string `xml:"LOGIN,omitempty" json:"login,omitempty"`
	ID    string `xml:"ID,omitempty" json:"id,omitempty"`
	Name  string `xml:"NAME,omitempty" json:"name,omitempty"`
	Home  string `xml:"HOME,omitempty" json:"home,omitempty"`
	Shell string `xml:"SHELL,omitempty" json:"shell,omitempty"`
}

type xmlLGroup struct {
	ID     string   `xml:"ID,omitempty" json:"id,omitempty"`
	Name   string   `xml:"NAME,omitempty" json:"name,omitempty"`
	Member []string `xml:"MEMBER,omitempty" json:"member,omitempty"`
}

type xmlUser struct {
	Login  string `xml:"LOGIN,omitempty" json:"login,omitempty"`
	Domain string `xml:"DOMAIN,omitempty" json:"domain,omitempty"`
}

type xmlProcess struct {
	User          string  `xml:"USER,omitempty" json:"user,omitempty"`
	PID           int32   `xml:"PID,omitempty" json:"pid,omitempty"`
	CPUUsage      float64 `xml:"CPUUSAGE,omitempty" json:"cpuusage,omitempty"`
	Mem           float32 `xml:"MEM,omitempty" json:"mem,omitempty"`
	VirtualMemory uint64  `xml:"VIRTUALMEMORY,omitempty" json:"virtualmemory,omitempty"`
	TTY           string  `xml:"TTY,omitempty" json:"tty,omitempty"`
	Started       string  `xml:"STARTED,omitempty" json:"started,omitempty"`
	Cmd           string  `xml:"CMD,omitempty" json:"cmd,omitempty"`
}

type xmlAccount struct {
	KeyName  string `xml:"KEYNAME,omitempty" json:"keyname,omitempty"`
	KeyValue string `xml:"KEYVALUE,omitempty" json:"keyvalue,omitempty"`
}
