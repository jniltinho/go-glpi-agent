## ADDED Requirements

### Requirement: Core Windows inventory coverage
The system SHALL collect operating system/build/edition, hostname/domain, architecture, boot/install time, timezone, BIOS/baseboard/chassis/UUID, CPU, total and physical memory, physical disks, logical volumes/filesystems, network interfaces/addresses, USB/PnP devices, installed software, local users/groups, active user, and optional processes on supported Windows releases.

#### Scenario: Inventory a Windows Server baseline
- **WHEN** the agent runs as LocalSystem on the Windows Server 2022 Vagrant baseline
- **THEN** every core category is present when the operating system exposes data and required identity fields are nonempty

### Requirement: Extended Windows inventory coverage
The system SHALL collect hotfixes, AppX packages, printers, monitors/EDID, display and storage controllers, video, batteries, sound/input/ports, antivirus/Defender status, and firewall profile state when those facilities exist on the endpoint.

#### Scenario: Category is unavailable on Server Core
- **WHEN** an extended facility such as a monitor or desktop antivirus namespace is absent
- **THEN** the collector reports the category as unavailable without failing the complete inventory cycle

### Requirement: Side-effect-free Windows data sources
The implementation SHALL prefer BCL APIs, `System.Management` WMI/CIM, and `Microsoft.Win32.Registry` using explicit registry views. It MUST NOT query `Win32_Product`, invoke MSI consistency checks, or execute arbitrary shell text assembled from collected or configured values.

#### Scenario: Enumerate installed MSI software
- **WHEN** the software collector inventories a machine with MSI applications
- **THEN** it reads uninstall registry data and does not instantiate or query `Win32_Product`

### Requirement: Complete software inventory
The software collector SHALL enumerate HKLM 64-bit and 32-bit uninstall views, loaded user hives, optional offline profiles when `scan-profiles` is enabled, AppX packages, and applicable hotfixes. It SHALL capture source, name, version, publisher, architecture, install date, user identity, and system/update classification when available.

#### Scenario: Collect machine and user software
- **WHEN** an x64 endpoint has one machine-wide application and one application registered in a loaded user hive
- **THEN** both appear once with their respective machine/user sources and architecture metadata

#### Scenario: Offline profile cannot be loaded
- **WHEN** `scan-profiles` is enabled but a profile hive cannot be loaded or the required privilege is unavailable
- **THEN** loaded-hive and machine software remain in the inventory, the omission is reported, and any hive loaded by the agent is safely unloaded

### Requirement: Normalized and deterministic data
All collectors SHALL trim strings, remove known placeholder/junk identity values, canonicalize dates, architecture, identifiers, booleans, and units, deduplicate entries with category-specific stable keys, and emit deterministic category ordering.

#### Scenario: Duplicate uninstall entries
- **WHEN** the same software name/version/architecture is present in overlapping registry sources
- **THEN** the inventory contains one canonical entry with deterministic source precedence

#### Scenario: Placeholder SMBIOS values
- **WHEN** WMI returns a known placeholder serial or manufacturer string
- **THEN** the placeholder is omitted and documented fallback identity rules are applied without inventing data

### Requirement: Permission-aware graceful degradation
Collectors SHALL distinguish unavailable, access-denied, timeout, malformed, and unexpected failures. Missing privilege or an absent Windows class MUST degrade only the affected category, while a policy-defined minimum inventory remains eligible for submission.

#### Scenario: Registry access is denied
- **WHEN** one protected registry source denies access
- **THEN** accessible sources are still collected and the report identifies the denied source without exposing its contents

### Requirement: Cancellable bounded queries
Every WMI/CIM or registry adapter SHALL expose asynchronous cancellation and a native timeout or bounded enumeration strategy. Query objects, registry handles, COM objects, and loaded user hives MUST be released on success, failure, timeout, and cancellation.

#### Scenario: Cancel a long WMI query
- **WHEN** service shutdown cancels a collector during a long management query
- **THEN** the query stops or reaches its native timeout within the configured grace period and all associated resources are released

### Requirement: Fixture-testable mappings
Windows API access SHALL be behind injectable adapters so parsers, mappings, normalization, deduplication, and error policies can be unit-tested from captured fixtures without live WMI or registry access.

#### Scenario: Test WMI mapping off Windows
- **WHEN** a unit test supplies a captured BIOS fixture through the adapter interface on a non-Windows build host
- **THEN** the same typed BIOS mapping and cleanup logic execute without contacting WMI

