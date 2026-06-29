package linux

import (
	"context"
	"net"
	"runtime"
	"strconv"
	"strings"

	gnet "github.com/shirou/gopsutil/v3/net"
	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/sysutil"
)

type networkCollector struct{}

func init() { collector.Register(networkCollector{}) }

func (networkCollector) Name() string                      { return "linux/networks" }
func (networkCollector) Category() string                  { return "network" }
func (networkCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

func (networkCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	ifaces, err := gnet.InterfacesWithContext(ctx)
	if err != nil {
		return err
	}

	gateway := defaultGateway()

	for _, ifc := range ifaces {
		// metadados comuns a todas as entradas desta interface
		base := inventory.Network{
			Description: ifc.Name,
			MACAddr:     ifc.HardwareAddr,
			Type:        ifaceType(ifc.Name, ifc.Flags),
			Status:      "Down",
			VirtualDev:  virtualFlag(ifc.Name),
			IPGateway:   gateway,
		}
		if hasFlag(ifc.Flags, "up") {
			base.Status = "Up"
		}
		if speed := sysutil.ReadFileTrim("/sys/class/net/" + ifc.Name + "/speed"); speed != "" && speed != "-1" {
			base.Speed = speed
		}
		if mtu := sysutil.ReadFileTrim("/sys/class/net/" + ifc.Name + "/mtu"); mtu != "" {
			base.MTU = mtu
		}

		// o agente Perl emite uma entrada NETWORKS por endereço IP.
		if len(ifc.Addrs) == 0 {
			inv.AddNetwork(base)
			continue
		}
		for _, addr := range ifc.Addrs {
			ip, ipNet, perr := net.ParseCIDR(addr.Addr)
			if perr != nil {
				continue
			}
			n := base
			if ip.To4() != nil {
				n.IPAddress = ip.String()
				n.IPMask = net.IP(ipNet.Mask).String()
				n.IPSubnet = ipNet.IP.String()
			} else {
				n.IPAddress6 = ip.String()
			}
			inv.AddNetwork(n)
		}
	}
	return nil
}

func hasFlag(flags []string, f string) bool {
	for _, v := range flags {
		if strings.EqualFold(v, f) {
			return true
		}
	}
	return false
}

func ifaceType(name string, flags []string) string {
	if hasFlag(flags, "loopback") || name == "lo" {
		return "loopback"
	}
	if sysutil.FileExists("/sys/class/net/" + name + "/wireless") {
		return "wifi"
	}
	if virtualFlag(name) == "1" {
		return "virtual"
	}
	return "ethernet"
}

// virtualFlag retorna "1" se a interface não tem um device físico associado.
func virtualFlag(name string) string {
	// /sys/class/net/<name>/device existe apenas para devices físicos.
	if sysutil.FileExists("/sys/class/net/" + name + "/device") {
		return "0"
	}
	if name == "lo" {
		return "1"
	}
	return "1"
}

// defaultGateway lê o gateway padrão de /proc/net/route.
func defaultGateway() string {
	lines := sysutil.SplitLines(sysutil.ReadFileTrim("/proc/net/route"))
	for i, line := range lines {
		if i == 0 {
			continue // header
		}
		fields := strings.Fields(line)
		if len(fields) < 3 {
			continue
		}
		// Destination == 00000000 indica rota default; Gateway no campo 3 (hex LE)
		if fields[1] == "00000000" {
			if gw := hexLEToIP(fields[2]); gw != "" {
				return gw
			}
		}
	}
	return ""
}

// hexLEToIP converte um endereço IPv4 em hex little-endian (como em
// /proc/net/route) para notação decimal pontilhada.
func hexLEToIP(hex string) string {
	if len(hex) != 8 {
		return ""
	}
	var octets [4]int
	for i := 0; i < 4; i++ {
		b, err := strconv.ParseInt(hex[i*2:i*2+2], 16, 0)
		if err != nil {
			return ""
		}
		octets[3-i] = int(b) // little-endian
	}
	if octets[0]+octets[1]+octets[2]+octets[3] == 0 {
		return ""
	}
	return strconv.Itoa(octets[0]) + "." + strconv.Itoa(octets[1]) + "." +
		strconv.Itoa(octets[2]) + "." + strconv.Itoa(octets[3])
}
