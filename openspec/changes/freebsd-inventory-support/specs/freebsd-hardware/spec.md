## ADDED Requirements

### Requirement: The agent SHALL collect CPU and memory on FreeBSD

The agent SHALL report CPU information (model, cores/threads, arch) via `gopsutil/cpu` and
sysctl (`hw.model`, `hw.ncpu`), and total/swap memory via `gopsutil/mem`.

#### Scenario: CPU and memory reported

- **WHEN** an inventory cycle runs on FreeBSD
- **THEN** `CPUS` contains at least one entry with a non-empty model name and a positive core count, and `HARDWARE.MEMORY` is the total RAM in MB

### Requirement: The agent SHALL collect BIOS, board, chassis and UUID via kenv smbios

The agent SHALL populate the BIOS section (system maker/product/serial, BIOS vendor/version/
date, board maker/product/serial) and the hardware UUID from the `kenv smbios.*` keys
(`smbios.system.*`, `smbios.bios.*`, `smbios.planar.*`, `smbios.chassis.*`), which require no
root. Placeholder values SHALL be filtered out via the shared junk filter, as on Linux/Windows.

#### Scenario: System identity on physical hardware

- **WHEN** the inventory runs on a machine whose firmware exposes SMBIOS via kenv
- **THEN** `BIOS.SSN`, `SMANUFACTURER`, `SMODEL` and `HARDWARE.UUID` are populated with real values

#### Scenario: Junk values filtered

- **WHEN** `kenv smbios.system.serial` returns a placeholder such as "To be filled by O.E.M." or all-zeros
- **THEN** that field is emitted empty rather than as fake data

#### Scenario: VM or firmware without SMBIOS

- **WHEN** the `smbios.*` kenv keys are absent (e.g. some VMs)
- **THEN** the collector emits whatever identity exists without failing the cycle

### Requirement: The agent SHALL collect disks, filesystems and USB on FreeBSD

The agent SHALL report physical disks (name, model, serial, size) via `geom disk list`
(falling back to `camcontrol`), mounted filesystems (UFS/ZFS) via `gopsutil/disk`, and USB
devices (vendor/product id, description) via `usbconfig`, skipping hubs.

#### Scenario: Physical disks reported

- **WHEN** the host has one or more disks
- **THEN** `STORAGES` contains one entry per disk with size in MB and, when available, the serial/ident

#### Scenario: Filesystems reported

- **WHEN** the host has mounted UFS or ZFS filesystems
- **THEN** `DRIVES` contains one entry per real filesystem with total/free space, excluding pseudo mounts

#### Scenario: USB devices reported

- **WHEN** a non-hub USB device is connected
- **THEN** `USBDEVICES` contains an entry with its vendor and product id
