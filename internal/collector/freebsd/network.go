//go:build freebsd

package freebsd

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

// networkCollector collects network interfaces via gopsutil/net, with the default
// gateway from `route -n get default`.
type networkCollector struct{}

func init() { collector.Register(networkCollector{}) }

func (networkCollector) Name() string                      { return "freebsd/networks" }
func (networkCollector) Category() string                  { return "network" }
func (networkCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "freebsd" }

// Collect emits one network entry per IP address (matching the Perl agent), or a
// single address-less entry for interfaces without IPs.
func (networkCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	ifaces, err := gnet.InterfacesWithContext(ctx)
	if err != nil {
		return err
	}

	gateway := ""
	if out, rerr := sysutil.RunContext(ctx, "route", "-n", "get", "default"); rerr == nil {
		gateway = parseRouteGateway(out)
	}

	for _, ifc := range ifaces {
		loopback := hasFlag(ifc.Flags, "loopback") || ifc.Name == "lo0"
		ifType := "ethernet"
		if loopback {
			ifType = "loopback"
		}
		base := inventory.Network{
			Description: ifc.Name,
			MACAddr:     ifc.HardwareAddr,
			Type:        ifType,
			Status:      "Down",
			IPGateway:   gateway,
		}
		if loopback {
			base.VirtualDev = "1"
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

// hasFlag reports whether the interface flag f is present (case-insensitive).
func hasFlag(flags []string, f string) bool {
	for _, v := range flags {
		if strings.EqualFold(v, f) {
			return true
		}
	}
	return false
}
