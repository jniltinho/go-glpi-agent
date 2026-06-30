# windows-os Specification

## Purpose

Collect Windows operating-system identity — product name, edition, version, build,
kernel, architecture and boot time — along with hostname, domain and timezone, for the
inventory's operating-system and hardware sections.

## Requirements

### Requirement: The agent SHALL collect Windows operating-system identity

On Windows the agent SHALL populate the operating-system and hardware sections with the
Windows product name, edition, version and build number, kernel version, architecture and
boot time. Values SHALL be sourced from `gopsutil/host` and, where missing, the registry
(`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`).

#### Scenario: OS section populated on Windows

- **WHEN** an inventory cycle runs on Windows 10/11 or Windows Server
- **THEN** `OPERATINGSYSTEM` carries a non-empty `FULL_NAME` (e.g. "Microsoft Windows Server 2022 Standard"), `VERSION`, `KERNEL_NAME=Windows`, an `ARCH`, and a `BOOT_TIME`

#### Scenario: Build number resolved from registry

- **WHEN** `gopsutil/host` does not expose the UBR/build
- **THEN** the agent reads `CurrentBuild`/`UBR` from the registry and reflects it in the OS version

### Requirement: The agent SHALL resolve hostname, domain and timezone on Windows

The agent SHALL report the computer name, its AD/workgroup domain, the FQDN, and the
timezone name plus current UTC offset.

#### Scenario: Domain-joined host

- **WHEN** the host is joined to an Active Directory domain
- **THEN** `OPERATINGSYSTEM.DNS_DOMAIN`/`HARDWARE.WORKGROUP` reflect the domain and `FQDN` includes it

#### Scenario: Timezone reported

- **WHEN** the inventory runs in a host set to a non-UTC timezone
- **THEN** `OPERATINGSYSTEM.TIMEZONE.NAME` and `TIMEZONE.OFFSET` (e.g. `-0300`) are populated, and a nameless timezone is dropped from the native JSON
