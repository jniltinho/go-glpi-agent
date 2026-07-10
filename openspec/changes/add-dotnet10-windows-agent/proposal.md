## Why

The repository has two mature Perl references and a cleaner Go reimplementation, but no Windows-first agent that combines the Perl agent's collection and MSI maturity with the Go agent's typed, testable architecture. A dedicated .NET 10 LTS project can use native Windows APIs, run as a real Windows Service, ship as an MSI, and be validated end to end against GLPI without coupling its lifecycle to the existing Go module.

## What Changes

- Add an independent `dotnet-glpi-agent/` subproject containing its own solution, source projects, tests, packaging, documentation, and test laboratory; the root Go agent and the two `base/` references remain unchanged.
- Build a Windows-only, self-contained .NET 10 agent with one-shot CLI commands and a long-running Windows Service, compatible with the established `agent.cfg` settings where practical.
- Introduce a typed inventory model and isolated, cancellable collectors inspired by the Go implementation, while porting the broader Windows coverage and field-cleaning behavior from the current GLPI Agent Perl implementation.
- Collect Windows OS, hardware, BIOS, CPU, memory, disks, volumes, network, USB/PnP, installed software, hotfixes, AppX packages, users/groups/sessions, processes, printers, monitors, video/controllers, batteries, antivirus, and firewall information without using `Win32_Product`.
- Send GLPI native CONTACT/inventory JSON with persistent agent/device identifiers and automatically fall back to legacy PROLOG/XML; also support deterministic local JSON/XML output for diagnostics and tests.
- Package the self-contained `win-x64` executable in a per-machine MSI built with WiX Toolset, installing and controlling a Windows Service, preserving configuration/state across upgrades, supporting silent deployment properties, clean uninstall, repair, rollback, and future code signing.
- Add a reproducible integration laboratory under the new project: Docker Compose for official GLPI and database images, automated inventory enablement/readiness, and a WinRM-provisioned Vagrant Windows VM that installs the MSI, runs the agent, compares it with the Perl and Go references, and verifies the resulting GLPI asset.
- Add unit, contract, schema, MSI lifecycle, and end-to-end acceptance tests, with CI build/test/package jobs and explicit manual gates for virtualization-dependent tests.

## Capabilities

### New Capabilities

- `dotnet-agent-core`: Independent .NET 10 solution, CLI/configuration, scheduling, identity persistence, typed inventory model, collector orchestration, logging, and local output.
- `dotnet-windows-inventory`: Windows-native inventory collectors, normalization, deduplication, graceful degradation, and coverage parity criteria derived from the Perl and Go agents.
- `dotnet-glpi-protocol`: GLPI native CONTACT/inventory JSON, schema normalization, HTTP/TLS behavior, persistent headers, legacy XML fallback, and protocol diagnostics.
- `dotnet-windows-service`: Windows Service hosting, periodic execution, cancellation, recovery, permissions, and Event Log/file logging behavior.
- `dotnet-windows-msi`: WiX MSI build, installation layout, service registration, configuration properties, upgrades, repairs, uninstalls, rollback, artifacts, checksums, and signing hooks.
- `dotnet-integration-lab`: Dockerized GLPI test stack, Vagrant Windows test host, reference-agent comparisons, schema validation, GLPI asset assertions, and end-to-end test orchestration.

### Modified Capabilities

None. The new project is additive and does not change the requirements of the existing Go agent.

## Impact

- **Repository layout:** new `dotnet-glpi-agent/` tree; no business logic is added to the root Go module or `base/` reference trees.
- **Toolchain:** .NET 10 SDK, Windows-targeted `System.Management`, Microsoft hosting/configuration packages, WiX Toolset 7 SDK, PowerShell, Vagrant with VirtualBox or Hyper-V, Docker Compose, and a Windows-capable packaging runner.
- **Runtime systems:** Windows 10/11 and Windows Server 2022/2025 targets; Windows Service Control Manager, WMI/CIM, registry, Event Log, and local machine/user profile data.
- **External integration:** official GLPI Docker images with MariaDB, the GLPI native inventory schema and endpoint, and legacy OCS/FusionInventory-compatible endpoints.
- **Distribution/security:** new MSI and checksum artifacts, optional Authenticode signing in release environments, protected `%ProgramFiles%` binaries and `%ProgramData%` configuration/state, and explicit handling of secrets in logs and installer properties.
- **Licensing:** the implementation must preserve attribution and avoid copying GPL-covered Perl code verbatim; the WiX Toolset 7 OSMF EULA/maintenance-fee terms require an explicit project decision before release packaging is adopted.
