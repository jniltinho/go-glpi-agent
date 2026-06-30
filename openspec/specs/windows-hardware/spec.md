# windows-hardware Specification

## Purpose

Collect Windows hardware inventory — CPU sockets, physical memory, BIOS/baseboard/chassis
identity and system UUID, physical and logical storage, and connected USB devices — sourced
from `gopsutil` and WMI, with placeholder/junk values filtered out.

## Requirements

### Requirement: The agent SHALL collect CPU and memory on Windows

The agent SHALL report one CPU entry per physical socket (name, manufacturer, speed, cores,
threads, arch) and total/swap memory, plus one memory entry per populated physical slot
(capacity, type, speed, serial, manufacturer) via WMI `Win32_PhysicalMemory`.

#### Scenario: CPU sockets reported

- **WHEN** an inventory cycle runs on a Windows host
- **THEN** `CPUS` contains one entry per socket with a non-empty model name and a positive core/thread count

#### Scenario: Physical memory slots reported

- **WHEN** WMI exposes installed DIMMs
- **THEN** `MEMORIES` contains one entry per populated slot with capacity in MB; empty slots are skipped

### Requirement: The agent SHALL collect BIOS, baseboard, chassis and system UUID on Windows

The agent SHALL populate the BIOS section (system manufacturer/model/serial, BIOS
vendor/version/date, asset tag, motherboard manufacturer/model/serial) and the hardware
UUID, sourced from WMI (`Win32_BIOS`, `Win32_ComputerSystem`, `Win32_BaseBoard`,
`Win32_SystemEnclosure`, `Win32_ComputerSystemProduct`). Placeholder values
("To be filled by O.E.M.", all-zero serials, etc.) SHALL be filtered out as on Linux.

#### Scenario: BIOS/system identity on physical hardware

- **WHEN** the inventory runs on a physical machine
- **THEN** `BIOS.SSN` (system serial), `SMANUFACTURER`, `SMODEL` and `HARDWARE.UUID` are populated with real values

#### Scenario: Junk values filtered

- **WHEN** WMI returns a placeholder serial such as `To be filled by O.E.M.` or `0000000`
- **THEN** that field is emitted empty rather than as fake data

#### Scenario: Virtual machine without SMBIOS passthrough

- **WHEN** the host is a VM whose chassis/board fields are placeholders
- **THEN** the collector emits whatever real identity exists (e.g. UUID, VM model) without failing the cycle

### Requirement: The agent SHALL collect storage, drives and USB on Windows

The agent SHALL report physical disks (model, serial, size, type, firmware) via WMI
`Win32_DiskDrive`/`MSFT_PhysicalDisk`, logical drives/filesystems (volume, mount, fs,
total, free) via `gopsutil/disk`, and connected USB devices (vendor/product id, name,
serial) via WMI `Win32_PnPEntity`, skipping hubs.

#### Scenario: Physical disks reported

- **WHEN** the host has one or more disks
- **THEN** `STORAGES` contains one entry per physical disk with size in MB and a type of NVMe/SSD/HDD

#### Scenario: Logical drives reported

- **WHEN** the host has mounted volumes (C:, D:, …)
- **THEN** `DRIVES` contains one entry per real volume with total/free space, excluding pseudo/virtual mounts

#### Scenario: USB devices reported

- **WHEN** a non-hub USB device is connected
- **THEN** `USBDEVICES` contains an entry with its vendor and product id
