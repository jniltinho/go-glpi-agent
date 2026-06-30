# macos-network Specification

## Purpose
TBD - created by archiving change 2026-06-30-macos-inventory-support. Update Purpose after archive.
## Requirements
### Requirement: The agent SHALL collect macOS network interfaces

On `GOOS=darwin` the agent SHALL populate the NETWORKS section from gopsutil/net,
emitting one entry per IP address (with an address-less fallback for interfaces without
an IP), carrying MAC, interface type (loopback/wifi/ethernet), status (Up/Down) and the
default gateway resolved via `route -n get default`.

#### Scenario: Interfaces and addresses reported

- **WHEN** the agent runs on macOS
- **THEN** NETWORKS lists each interface with its MAC, type and status, one entry per IPv4/IPv6 address, and the active interface carries the default gateway

#### Scenario: Interface type classification

- **WHEN** the host has a loopback (`lo0`), a wired and/or a Wi-Fi interface
- **THEN** each is classified as `loopback`, `ethernet` or `wifi` respectively

