# windows-network Specification

## Purpose

Collect Windows network interfaces — addresses, MAC, type, status, speed, MTU, virtual flag
and default gateway — sourced from `gopsutil/net` and enriched by WMI, emitting one entry
per IP address as on Linux.

## Requirements

### Requirement: The agent SHALL collect network interfaces on Windows

The agent SHALL report network interfaces with their description, MAC address, type
(ethernet/wifi/loopback/virtual), up/down status, speed, MTU, virtual flag and the default
gateway. Addresses SHALL come from `gopsutil/net`, enriched by WMI
`Win32_NetworkAdapter`/`Win32_NetworkAdapterConfiguration` for type, gateway and speed.
As on Linux, one entry SHALL be emitted per IP address, or a single address-less entry for
interfaces without IPs.

#### Scenario: Active interface with IPv4

- **WHEN** an enabled adapter has an IPv4 address
- **THEN** `NETWORKS` contains an entry with `IPADDRESS`, `IPMASK`, `IPSUBNET`, `MACADDR`, `STATUS=Up`, and the host default gateway

#### Scenario: IPv6 address

- **WHEN** an adapter has an IPv6 address
- **THEN** the entry carries `IPADDRESS6` and leaves the IPv4 fields empty

#### Scenario: Native JSON type/boolean coercion

- **WHEN** the inventory is serialized as native GLPI JSON
- **THEN** `status` is lowercased, `virtualdev` is a boolean, `mtu` is an integer, and a `virtual` type maps to `ethernet`
