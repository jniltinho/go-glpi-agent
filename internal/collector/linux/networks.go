package linux

import (
	"context"
	"net"
	"runtime"
	"strconv"
	"strings"

	gnet "github.com/shirou/gopsutil/v3/net"
	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// networkCollector collects network interfaces and their addresses via
// gopsutil/net, enriched with speed/MTU/type from /sys/class/net and the
// default gateway from /proc/net/route.
type networkCollector struct{}

// init registers the network collector with the collector registry.
func init() { collector.Register(networkCollector{}) }

// Name returns the collector's registry name.
func (networkCollector) Name() string { return "linux/networks" }

// Category returns the inventory section this collector fills.
func (networkCollector) Category() string { return "network" }

// IsEnabled reports whether the collector should run; it is Linux-only.
func (networkCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "linux" }

// Collect emits one network entry per IP address (matching the Perl agent),
// or a single address-less entry for interfaces without IPs. Each entry carries
// the interface metadata: MAC, type, status, speed, MTU, virtual flag, gateway.
func (networkCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	ifaces, err := gnet.InterfacesWithContext(ctx)
	if err != nil {
		return err
	}

	gateway := defaultGateway()

	for _, ifc := range ifaces {
		// metadata common to all entries of this interface
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

		// the Perl agent emits one NETWORKS entry per IP address.
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

// hasFlag reports whether the interface flag f is present (case-insensitive).
func hasFlag(flags []string, f string) bool {
	for _, v := range flags {
		if strings.EqualFold(v, f) {
			return true
		}
	}
	return false
}

// ifaceType classifies an interface as loopback, wifi, virtual or ethernet
// using its flags, name and presence under /sys/class/net.
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

// virtualFlag returns "1" if the interface has no associated physical device.
func virtualFlag(name string) string {
	// /sys/class/net/<name>/device exists only for physical devices.
	if sysutil.FileExists("/sys/class/net/" + name + "/device") {
		return "0"
	}
	if name == "lo" {
		return "1"
	}
	return "1"
}

// defaultGateway reads the default gateway from /proc/net/route.
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
		// Destination == 00000000 indicates the default route; Gateway in field 3 (hex LE)
		if fields[1] == "00000000" {
			if gw := hexLEToIP(fields[2]); gw != "" {
				return gw
			}
		}
	}
	return ""
}

// hexLEToIP converts a little-endian hex IPv4 address (as in
// /proc/net/route) to dotted-decimal notation.
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
