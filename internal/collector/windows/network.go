//go:build windows

package windows

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
)

// networkCollector collects network interfaces and their addresses via
// gopsutil/net, enriched with speed/virtual flag from Win32_NetworkAdapter and
// the default gateway from Win32_NetworkAdapterConfiguration.
type networkCollector struct{}

// init registers the network collector with the collector registry.
func init() { collector.Register(networkCollector{}) }

// Name returns the collector's registry name.
func (networkCollector) Name() string { return "windows/networks" }

// Category returns the inventory section this collector fills.
func (networkCollector) Category() string { return "network" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (networkCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

type win32NetworkAdapter struct {
	NetConnectionID string
	Speed           uint64
	PhysicalAdapter bool
}

type win32NetworkAdapterConfiguration struct {
	IPEnabled        bool
	DefaultIPGateway []string
}

// adapterMeta is the per-adapter metadata keyed by friendly connection name.
type adapterMeta struct {
	speed    string
	physical bool
}

// Collect emits one network entry per IP address (matching the Perl agent), or a
// single address-less entry for interfaces without IPs. Metadata (speed, virtual
// flag, gateway) comes from WMI.
func (networkCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	ifaces, err := gnet.InterfacesWithContext(ctx)
	if err != nil {
		return err
	}

	meta := adapterMetaByName()
	gateway := defaultGateway()

	for _, ifc := range ifaces {
		m := meta[ifc.Name]
		base := inventory.Network{
			Description: ifc.Name,
			MACAddr:     ifc.HardwareAddr,
			Type:        ifaceType(ifc.Name, ifc.Flags, m.physical),
			Status:      "Down",
			VirtualDev:  virtualFlag(m.physical),
			Speed:       m.speed,
			IPGateway:   gateway,
		}
		if hasFlag(ifc.Flags, "up") {
			base.Status = "Up"
		}

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

// adapterMetaByName builds a friendly-name → metadata map from Win32_NetworkAdapter.
func adapterMetaByName() map[string]adapterMeta {
	out := map[string]adapterMeta{}
	var adapters []win32NetworkAdapter
	if err := queryWMI("SELECT NetConnectionID, Speed, PhysicalAdapter FROM Win32_NetworkAdapter", &adapters); err != nil {
		return out
	}
	for _, a := range adapters {
		if a.NetConnectionID == "" {
			continue
		}
		speed := ""
		if a.Speed != 0 {
			speed = strconv.FormatUint(a.Speed/1000/1000, 10) // bps -> Mb/s
		}
		out[a.NetConnectionID] = adapterMeta{speed: speed, physical: a.PhysicalAdapter}
	}
	return out
}

// defaultGateway returns the first non-empty default gateway across all enabled
// adapter configurations.
func defaultGateway() string {
	var cfgs []win32NetworkAdapterConfiguration
	if err := queryWMI("SELECT IPEnabled, DefaultIPGateway FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE", &cfgs); err != nil {
		return ""
	}
	for _, c := range cfgs {
		if len(c.DefaultIPGateway) > 0 && c.DefaultIPGateway[0] != "" {
			return c.DefaultIPGateway[0]
		}
	}
	return ""
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

// ifaceType classifies an interface as loopback, wifi, virtual or ethernet using
// its flags, name and the WMI physical-adapter flag.
func ifaceType(name string, flags []string, physical bool) string {
	if hasFlag(flags, "loopback") {
		return "loopback"
	}
	low := strings.ToLower(name)
	if strings.Contains(low, "wi-fi") || strings.Contains(low, "wireless") {
		return "wifi"
	}
	if !physical {
		return "virtual"
	}
	return "ethernet"
}

// virtualFlag returns "1" for non-physical adapters, "0" otherwise.
func virtualFlag(physical bool) string {
	if physical {
		return "0"
	}
	return "1"
}
