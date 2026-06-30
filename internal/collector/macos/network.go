//go:build darwin

package macos

import (
	"context"
	"net"
	"runtime"
	"strings"

	gnet "github.com/shirou/gopsutil/v3/net"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// networkCollector collects network interfaces via gopsutil/net, enriched with
// the hardware-port description / physical-vs-virtual flag from
// `networksetup -listallhardwareports` and the default gateway from
// `route -n get default`. It emits one entry per interface (combining IPv4 and
// IPv6), matching the official macOS agent (MacOS/Networks.pm).
type networkCollector struct{}

func init() { collector.Register(networkCollector{}) }

func (networkCollector) Name() string                      { return "macos/networks" }
func (networkCollector) Category() string                  { return "network" }
func (networkCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "darwin" }

// Collect adds one NETWORKS entry per interface. The hardware port name and the
// physical/virtual flag come from networksetup; the type is derived from that
// description; the default gateway is attached only to the interface whose subnet
// contains it (the official agent's isSameNetwork rule).
func (networkCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	ifaces, err := gnet.InterfacesWithContext(ctx)
	if err != nil {
		return err
	}

	ports := map[string]hwPort{}
	if out, perr := sysutil.RunContext(ctx, "networksetup", "-listallhardwareports"); perr == nil {
		ports = parseNetworkSetup(out)
	}

	gateway := ""
	if out, rerr := sysutil.RunContext(ctx, "route", "-n", "get", "default"); rerr == nil {
		gateway = parseRouteGateway(out)
	}
	gwIP := net.ParseIP(gateway)

	for _, ifc := range ifaces {
		port, isPhysical := ports[ifc.Name]

		desc := ifc.Name
		if port.Description != "" {
			desc = port.Description
		}
		mac := ifc.HardwareAddr
		if mac == "" {
			mac = port.MAC
		}

		n := inventory.Network{
			Description: desc,
			MACAddr:     mac,
			Type:        ifaceTypeFromPort(port.Description, ifc.Name),
			Status:      "Down",
			VirtualDev:  "1",
		}
		if isPhysical {
			n.VirtualDev = "0"
		}
		if hasFlag(ifc.Flags, "up") {
			n.Status = "Up"
		}

		// One entry per interface: first IPv4 (with mask/subnet) + first IPv6.
		for _, addr := range ifc.Addrs {
			ip, ipNet, perr := net.ParseCIDR(addr.Addr)
			if perr != nil {
				continue
			}
			if ip.To4() != nil {
				if n.IPAddress == "" {
					n.IPAddress = ip.String()
					n.IPMask = net.IP(ipNet.Mask).String()
					n.IPSubnet = ipNet.IP.String()
					if gwIP != nil && ipNet.Contains(gwIP) {
						n.IPGateway = gateway
					}
				}
			} else if n.IPAddress6 == "" {
				n.IPAddress6 = ip.String()
			}
		}
		inv.AddNetwork(n)
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
