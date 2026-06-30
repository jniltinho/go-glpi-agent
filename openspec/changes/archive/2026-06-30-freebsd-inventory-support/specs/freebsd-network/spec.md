## ADDED Requirements

### Requirement: The agent SHALL collect network interfaces on FreeBSD

The agent SHALL report network interfaces with their description, MAC address, type
(ethernet/loopback/virtual), up/down status, MTU and the default gateway. Addresses SHALL
come from `gopsutil/net`; the default gateway SHALL come from the routing table
(`netstat -rn` / `route -n get default`). As on Linux, one entry SHALL be emitted per IP
address, or a single address-less entry for interfaces without IPs.

#### Scenario: Active interface with IPv4

- **WHEN** an enabled interface has an IPv4 address
- **THEN** `NETWORKS` contains an entry with `IPADDRESS`, `IPMASK`, `IPSUBNET`, `MACADDR`, `STATUS=Up`, and the host default gateway

#### Scenario: IPv6 address

- **WHEN** an interface has an IPv6 address
- **THEN** the entry carries `IPADDRESS6` and leaves the IPv4 fields empty

#### Scenario: Native JSON type/boolean coercion

- **WHEN** the inventory is serialized as native GLPI JSON
- **THEN** `status` is lowercased, `virtualdev` is a boolean and `mtu` is an integer, matching the schema
