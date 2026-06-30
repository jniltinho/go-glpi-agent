## ADDED Requirements

### Requirement: The agent SHALL collect macOS CPU, memory, disks and USB

On `GOOS=darwin` the agent SHALL populate CPU, memory, storage (physical disks and
filesystems) and USB sections using `system_profiler` (`SPHardwareDataType`,
`SPMemoryDataType`, `SPNVMeDataType`, `SPSerialATADataType`, `SPUSBDataType`), `sysctl`
(`machdep.cpu.*`, `hw.*`) and gopsutil (cpu/mem/disk), working on both Intel and Apple
Silicon.

#### Scenario: CPU on Intel and Apple Silicon

- **WHEN** the agent runs on an Intel Mac
- **THEN** it reports the CPU brand (from `machdep.cpu.brand_string`), physical core count, logical/thread count, speed and arch
- **WHEN** the agent runs on an Apple Silicon Mac (where `machdep.cpu.brand_string` is absent)
- **THEN** it reports the chip name from `system_profiler SPHardwareDataType` / `hw.model` and the core/thread counts from `hw.physicalcpu`/`hw.logicalcpu`

#### Scenario: Memory total and slots

- **WHEN** the agent runs
- **THEN** HARDWARE.MEMORY and HARDWARE.SWAP are set from gopsutil/mem, and MEMORIES carries per-slot entries from `SPMemoryDataType` (a single unified entry on Apple Silicon)

#### Scenario: Disks and USB

- **WHEN** the agent runs
- **THEN** STORAGES lists physical disks (model/serial/size/type) from `SPNVMeDataType`/`SPSerialATADataType`, DRIVES lists real mounted filesystems (pseudo mounts skipped), and USBDEVICES lists non-hub USB devices from `SPUSBDataType`

### Requirement: The agent SHALL resolve a stable system serial and UUID with fallbacks

On `GOOS=darwin` the agent SHALL populate the BIOS section (manufacturer, model, serial,
boot-ROM version) and HARDWARE.UUID using `system_profiler SPHardwareDataType` with an
`ioreg IOPlatformExpertDevice` fallback, mirroring the official agent. The serial SHALL be
resolved through the chain `Serial Number` → `Serial Number (system)` →
`IOPlatformSerialNumber`, and the UUID through `Hardware UUID` → `IOPlatformUUID`. All
identity strings SHALL pass through `sysutil.CleanDMI`. When no real serial is obtainable
but a UUID exists, the agent SHALL use the UUID as the serial so the host is never reported
without a serial.

#### Scenario: Real serial and UUID present

- **WHEN** `system_profiler SPHardwareDataType` returns a serial and a Hardware UUID
- **THEN** BIOS.SSN is the serial, HARDWARE.UUID is the UUID, BIOS.SMANUFACTURER is "Apple Inc." (or the `ioreg` manufacturer), and BIOS.SMODEL is the model identifier

#### Scenario: Serial redacted, UUID available (virtualized / CI runner)

- **WHEN** `system_profiler` and `ioreg` yield no serial (or only a junk/zeroed value filtered by `CleanDMI`) but a UUID is present
- **THEN** BIOS.SSN falls back to the UUID and HARDWARE.UUID is still set, so the inventory is never serial-less

#### Scenario: ioreg fallback when system_profiler lacks identity

- **WHEN** `system_profiler SPHardwareDataType` does not report `Hardware UUID`/serial
- **THEN** the agent reads `IOPlatformUUID` and `IOPlatformSerialNumber` from `ioreg -d2 -c IOPlatformExpertDevice`

#### Scenario: No identity at all

- **WHEN** neither a serial nor a UUID can be obtained
- **THEN** the agent omits the serial rather than emitting a placeholder, and the inventory still validates against GLPI's schema
