# Windows collector coverage

This matrix records the clean-room behavior sources used by the .NET implementation. The reference columns describe observed behavior only; no Perl or GPL implementation is copied into this project.

| Category | .NET source | Principal fields | Perl / official GLPI behavior retained | Root Go behavior retained |
| --- | --- | --- | --- | --- |
| Operating system | `Win32_OperatingSystem`, Windows version registry, BCL host/time | caption, edition, version/build/UBR, architecture, install/boot time, host/domain, timezone | Detailed Windows edition and version reporting | Typed record and shared normalization |
| Hardware, BIOS, baseboard, chassis | `Win32_ComputerSystem`, `Win32_ComputerSystemProduct`, `Win32_BIOS`, `Win32_BaseBoard`, `Win32_SystemEnclosure` | manufacturer, model, UUID, serial, asset tag, chassis | Placeholder filtering and SMBIOS fallbacks | Stable identifiers and collector isolation |
| CPU | `Win32_Processor` | socket, cores, threads, speed, manufacturer, architecture | Windows processor mapping | Typed numeric fields |
| Memory | `Win32_PhysicalMemory`, `Win32_PhysicalMemoryArray`, OS totals | capacity, speed, type, part/serial, populated and empty slots | Slot-level reporting | Canonical units and deterministic order |
| Storage and controllers | `Win32_DiskDrive`, SCSI/IDE controller classes | model, serial, firmware, capacity, interface, media, controller status | NVMe/SSD/HDD detail | Typed capacities and stable PnP identity |
| Volumes | `DriveInfo`, system drive | mount/letter, label, filesystem, type, total/free, ready, system | Ready and addressable drive behavior | BCL-first collection |
| Network | `NetworkInterface` enriched by adapter/configuration WMI | interface identity, MAC, status, type, speed, DHCP, address/prefix, gateway, DNS | Per-address inventory and disconnected adapters | Joined typed data and deterministic deduplication |
| USB and PnP | `Win32_PnPEntity` | PnP identity, VID/PID/serial, class, status, connected state | Hub suppression and USB parsing | Category-specific stable keys |
| Users, groups, sessions | account and logon WMI classes | SID, account/domain, local/domain, enabled, membership, session | LocalSystem-visible active sessions | Typed identities and omission isolation |
| Processes (opt-in) | BCL process API plus `Win32_Process.GetOwner` | PID, owner, command, start time, memory | Owner and command visibility with access-denied degradation | Explicit opt-in, redaction, and size caps |
| Installed software | HKLM native/WOW64 uninstall views, loaded HKU, optional offline `NTUSER.DAT` | name, version, publisher, architecture, date, size, URL/uninstall metadata, user, system/update flags | Per-profile and 32/64-bit coverage without `Win32_Product` | Deterministic source precedence |
| App packages | `Windows.Management.Deployment.PackageManager` by profile SID | package full name, name, version, publisher, architecture, user, framework | Per-user AppX coverage | Shell-free injectable adapter |
| Hotfixes | `Win32_QuickFixEngineering` | KB ID, description, installer, date, classification | Security/hotfix/update classification | Deterministic typed records |
| Printers | `Win32_Printer` | driver, port, status, default, network, shared | Local/network printer coverage | Stable identity and optional category |
| Monitors | EDID-backed `WmiMonitorID`, registry EDID fallback | manufacturer, model, serial, resolution, manufacture date | EDID identity fallback | Pure EDID parser with fixture tests |
| Video and peripherals | video, sound, keyboard, pointing, serial/parallel/modem WMI classes | stable PnP ID, names, vendor, driver, memory, resolution, type/status | Broad peripheral coverage | One bounded collector and typed categories |
| Battery | `Win32_Battery` | design/full capacity, charge, chemistry, voltage, status | Portable power state | Empty result is valid on battery-less hosts |
| Antivirus | `SecurityCenter2` and Microsoft Defender management namespace | product, signature version, enabled/current state, source | Desktop security-center plus server Defender fallback | Per-source diagnostics on Server Core |
| Firewall | `MSFT_NetFirewallProfile` | domain/private/public enablement and default actions | Windows profile state | Missing namespace is an unavailable category, not a failed cycle |

All WMI queries select only required properties, set a native enumeration timeout, and dispose management objects. Registry reads use explicit views and disposable keys. Collectors that encounter absent desktop-only namespaces return source diagnostics so a Server Core inventory remains usable.
