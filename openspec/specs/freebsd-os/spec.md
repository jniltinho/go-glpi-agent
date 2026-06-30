# freebsd-os Specification

## Purpose

Collect the FreeBSD operating-system identity, hostname/domain and timezone for
the inventory's operating-system and hardware sections.

## Requirements

### Requirement: The agent SHALL collect FreeBSD operating-system identity

On FreeBSD the agent SHALL populate the operating-system and hardware sections with the
OS name (FreeBSD), release/version, kernel version, architecture and boot time, sourced
from `gopsutil/host` and sysctl (`kern.ostype`, `kern.osrelease`, `hw.machine_arch`).

#### Scenario: OS section populated on FreeBSD

- **WHEN** an inventory cycle runs on FreeBSD 14
- **THEN** `OPERATINGSYSTEM` carries `NAME`/`FULL_NAME` containing "FreeBSD", a `VERSION` (e.g. "14.1-RELEASE"), `KERNEL_NAME=FreeBSD`, an `ARCH`, and a `BOOT_TIME`

### Requirement: The agent SHALL resolve hostname, domain and timezone on FreeBSD

The agent SHALL report the host name, DNS domain and FQDN (via the cross-platform hostname
collector), and the timezone name plus current UTC offset. The timezone name SHALL be read
from `/var/db/zoneinfo` (FreeBSD has no `/etc/timezone` and `/etc/localtime` is a copy, not
a symlink).

#### Scenario: Timezone reported on FreeBSD

- **WHEN** the host has a non-UTC timezone configured (e.g. via `tzsetup`)
- **THEN** `OPERATINGSYSTEM.TIMEZONE.NAME` reflects the `/var/db/zoneinfo` value (e.g. "America/Sao_Paulo") and `TIMEZONE.OFFSET` is populated (e.g. "-0300")

#### Scenario: Hostname/domain reported

- **WHEN** the inventory runs on a host with a configured domain
- **THEN** `OPERATINGSYSTEM.FQDN` and `HARDWARE.DNS`/`DNS_DOMAIN` are populated
