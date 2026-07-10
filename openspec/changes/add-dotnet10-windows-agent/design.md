## Context

This change creates a new Windows-focused agent beside the existing Go module. The implementation is intentionally isolated under `dotnet-glpi-agent/`; `base/perl/` and `base/glpi-agent/` remain read-only behavioral references, while the root Go project remains independently buildable and releasable.

The reference review produced three complementary sources of behavior:

| Reference | Assets to retain | Limits to avoid |
| --- | --- | --- |
| `base/perl/` (FusionInventory) | Long-lived OCS/XML compatibility, mature Windows inventory edge cases, service behavior | Older protocol as the primary path, dynamic/untyped data flow, Perl runtime and packaging complexity |
| `base/glpi-agent/` (GLPI Agent 1.19-dev) | Native CONTACT/inventory JSON, schema-driven normalization, persistent agent ID, per-profile software, hotfix/AppX coverage, Windows Service and MSI upgrade/configuration behavior | Very broad task surface, global state, extensive custom installer actions, direct code reuse constrained by GPLv2+ |
| root Go project | Typed single-source inventory model, small collector interface, per-collector isolation, native JSON plus legacy XML fallback, deterministic parsers, Docker/Vagrant comparison workflow | Narrower Windows coverage, scheduled task instead of a service, ZIP rather than MSI |

The current toolchain supports the intended direction: .NET 10 is an LTS release supported through November 2028, .NET Worker Services integrate with the Windows Service Control Manager, and self-contained Windows publishing avoids a separately installed runtime. WiX Toolset 7.0 was released in April 2026 and supports SDK-style `.wixproj` builds, but its OSMF EULA requires explicit acceptance and can require a maintenance fee depending on revenue.

The repository convention is English for implementation code and technical documentation. The new project inherits the root MIT license for original code, while behavioral comparisons to the GPL references must be clean-room: requirements and observed outputs can be reused, but Perl source must not be translated line for line.

## Goals / Non-Goals

**Goals:**

- Deliver a self-contained .NET 10 Windows inventory agent, a real Windows Service, and a distributable `win-x64` MSI.
- Combine the broad Windows coverage and deployment behavior of the current Perl agent with the typed model, modular collectors, failure isolation, and testability of the Go implementation.
- Treat GLPI native JSON as the primary GLPI 10/11 protocol and legacy XML/PROLOG as a compatibility fallback.
- Keep the model independent from serializers so JSON and XML consume the same collected data.
- Support one-shot local inventory, one-shot server submission, and periodic service execution from the same executable.
- Provide reproducible unit, schema, packaging, Vagrant, Docker, and GLPI asset acceptance tests.
- Preserve configuration and agent identity across MSI upgrades and normal uninstalls.

**Non-Goals:**

- Replacing, embedding, or restructuring the root Go agent.
- Modifying either Perl reference project.
- Implementing GLPI deploy, network discovery/inventory, Wake-on-LAN, ESX, collect, or remote-inventory tasks in the first release; CONTACT advertises only inventory.
- Supporting Linux, FreeBSD, or macOS from the .NET project.
- Requiring Native AOT or trimming in the first release; Windows management and configuration APIs favor compatibility over minimum binary size.
- Running nested Vagrant virtualization on ordinary hosted CI runners; the full lab runs on a dedicated test host or compatible self-hosted runner.
- Claiming exact item-count equality with the reference agents when Windows APIs legitimately expose different transient data.

## Decisions

### D1: Keep the .NET project physically and logically independent

Create this structure:

```text
dotnet-glpi-agent/
├── DotnetGlpiAgent.sln
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── src/
│   ├── DotnetGlpiAgent.Core/
│   ├── DotnetGlpiAgent.Windows/
│   ├── DotnetGlpiAgent.Protocol/
│   └── DotnetGlpiAgent.App/
├── tests/
│   ├── DotnetGlpiAgent.Core.Tests/
│   ├── DotnetGlpiAgent.Windows.Tests/
│   ├── DotnetGlpiAgent.Protocol.Tests/
│   └── DotnetGlpiAgent.IntegrationTests/
├── packaging/wix/
├── test/glpi/
├── test/vagrant-windows/
└── docs/
```

`Core` owns the domain model and contracts; `Windows` owns all Windows API access; `Protocol` owns serializers and server clients; `App` is the composition root, CLI, and service host. Project references point inward and do not reference the Go module or Perl trees.

Alternatives considered:

- A subdirectory inside the Go `internal/` tree was rejected because it would mix build systems and release lifecycles.
- A separate repository was rejected for now because the user requested the project here and local behavioral/test references are valuable.

### D2: Target .NET 10 LTS with compatibility-first publishing

Target `net10.0-windows` and pin the SDK through `global.json`. Publish `win-x64` self-contained so endpoints do not need a machine-wide .NET runtime. The MSI packages the normal self-contained publish directory; trimming and Native AOT remain disabled until all WMI, COM, Event Log, configuration, and serialization paths pass a dedicated publish-mode matrix.

A portable single-file artifact can be added after compatibility tests. It is not the MSI's source layout because native-library extraction under a service identity introduces avoidable startup and directory-permission concerns.

Alternatives considered:

- Framework-dependent deployment was rejected because it adds an endpoint prerequisite and complicates offline rollout.
- Native AOT was rejected for v1 because dynamic Windows management APIs and serializers require more compatibility work and do not improve inventory correctness.
- .NET 8 was rejected because the requested .NET 10 is now LTS and has the longer support horizon.

### D3: Use a typed model and explicit collector contracts

Define immutable or narrowly mutable records for the GLPI inventory sections and an `IInventoryCollector` contract with name, category, support check, and asynchronous collection. Collectors return typed contributions rather than mutating serializers or emitting protocol-specific maps. A single assembler normalizes and merges contributions into an inventory snapshot.

The orchestrator runs a bounded number of collectors concurrently. Each collector receives a `CancellationToken`, a configured deadline, and structured logging scope. A timeout or category failure is recorded in a collection report but does not cancel unrelated collectors. Shared Windows query adapters are injectable, making registry/WMI parsers unit-testable on non-Windows build hosts with fixtures.

This adopts the Go model/collector strengths while avoiding unbounded detached work: an adapter that cannot honor cancellation must also have a native query timeout and be observed until completion before its resources are disposed.

### D4: Prefer Windows APIs, using different sources by data category

Use `System.Management` WMI/CIM queries for SMBIOS, enclosure, memory slots, storage, PnP, printers, hotfixes, users, and other management classes. Use `Microsoft.Win32.Registry` with explicit 32-bit and 64-bit views for OS details and uninstall entries. Use BCL APIs for network interfaces, processes, filesystem volumes, time, and host identity where they provide better typed behavior.

The initial coverage matrix includes:

- OS/build/edition, boot/install time, architecture, hostname, domain, and timezone.
- BIOS, baseboard, chassis, UUID, asset tag, CPU, total and physical memory slots.
- Physical disks, logical volumes, filesystems, controllers, USB/PnP, video, monitors/EDID, batteries, sound/input/ports when exposed.
- Network interfaces, IP addresses, routes/gateways, DNS, DHCP, MAC, speed, status, virtual/physical classification.
- Software from HKLM 32/64 views, loaded HKEY_USERS hives, optional offline profiles, AppX packages, and hotfixes.
- Local users/groups, active/logged-on users, processes, printers, antivirus/Defender, and firewall profile state.

All identity strings pass a shared junk-value cleaner derived from observed behavior, not copied implementation. Duplicate rows use stable category-specific keys and deterministic ordering. `Win32_Product` is forbidden because it is slow and can trigger MSI repair.

Offline user-hive loading is opt-in (`scan-profiles`) and only runs with the required privilege. Every loaded hive is tracked and unloaded in `finally`, and a failure degrades to loaded-profile coverage.

### D5: Implement native GLPI first and conservative legacy fallback

The server client sends a native JSON CONTACT containing the persistent device ID, agent name/version, tag, and only the `inventory` task. It handles GLPI pending responses using the request ID and bounded server-specified delay, then sends schema-normalized inventory JSON with `GLPI-Agent-ID`.

Fallback to PROLOG plus compressed XML occurs only when the target explicitly does not support the native protocol or returns a recognizable legacy response. TLS validation failures, authentication failures, rate limiting, and server errors do not silently downgrade because doing so can hide configuration errors or duplicate submissions.

The transport supports proxy configuration, standard authentication, optional OAuth2 client credentials for GLPI 11, system/custom trust stores, optional client certificate authentication, request timeouts, bounded retries for idempotent contact/poll operations, zlib/gzip/no compression, and redacted structured diagnostics. JSON normalization follows the schema copied or extracted from the tested GLPI container rather than a hand-maintained approximation.

### D6: Preserve familiar configuration and identity semantics

The default locations are:

- Binary/runtime files: `%ProgramFiles%\DotnetGlpiAgent\`.
- Configuration, state, and logs: `%ProgramData%\DotnetGlpiAgent\` with administrator/SYSTEM ACLs.
- Main configuration: `%ProgramData%\DotnetGlpiAgent\agent.cfg`.

Configuration precedence is command line, `GLPI_AGENT_*` environment variables, `agent.cfg` plus includes, then defaults. Implement the commonly used Perl/Go keys first (`server`, `local`, `tag`, `delaytime`, `lazy`, `force`, `backend-collect-timeout`, `no-category`, `scan-processes`, `scan-profiles`, proxy/auth/TLS, logger/log file, and compression). Unknown keys generate a warning without exposing values.

Agent ID is a cryptographically generated UUID persisted atomically. Device ID uses compatible hostname/time semantics and is also persisted. The importer can read the existing Go state files and, where feasible without executing Perl, recognize GLPI/FusionInventory dump-derived identifiers supplied through an explicit migration file. Identity state is never regenerated merely because the MSI is upgraded, repaired, or normally removed.

### D7: One executable hosts CLI and a real Windows Service

`DotnetGlpiAgent.App` uses the Generic Host and `Microsoft.Extensions.Hosting.WindowsServices`. Commands include `run`, `validate-config`, and `version`; service mode is selected by the service command line/Windows Service lifetime rather than a separate executable.

The service performs a randomized initial delay, executes one inventory cycle at a time, waits the configured interval, and responds to SCM stop/shutdown with cancellation and a bounded grace period. Overlapping cycles are forbidden. Event Log receives service-level messages and a protected rolling file contains diagnostics. MSI configures delayed automatic start and SCM recovery restart actions.

A Scheduled Task mode is not part of v1 because the request explicitly benefits from a service and .NET provides first-class service hosting. A future task mode can be added without changing collectors.

### D8: Build a per-machine WiX 7 MSI with minimal custom actions

Use an SDK-style WiX Toolset 7 project to install the self-contained publish output per machine, register/start/stop the service through MSI service tables, write only first-install configuration defaults, and retain user-edited configuration/state. A stable package identity supports major upgrades and blocks downgrades. Normal uninstall removes binaries and the service but preserves `%ProgramData%`; explicit `PURGE=1` removes state after a clear warning.

Public properties support unattended fleet deployment for non-secret values such as `SERVER`, `TAG`, `INSTALLDIR`, `STARTSERVICE`, and `RUNNOW`. Secrets are provisioned through a protected configuration mechanism rather than ordinary MSI command-line properties. MSI-native tables and WiX extensions are preferred over executable custom actions; any unavoidable elevated action must validate paths, be rollback-safe, and have explicit install/repair/upgrade/uninstall conditions.

The release job produces versioned MSI, portable artifact if enabled, SHA-256 checksums, SBOM, and signing inputs. Authenticode signing of the executable and MSI plus trusted timestamping is required for a production release, but tests can use an unsigned development package.

WiX 7's OSMF EULA is a release gate. If its terms are not accepted, implementation must select a reviewed alternative before authoring the installer; silently downgrading to an unsupported WiX version is not acceptable.

### D9: Make Docker plus Vagrant an executable acceptance environment

Keep all new test assets inside `dotnet-glpi-agent/test/`. Docker Compose starts pinned official `glpi/glpi` and MariaDB images, waits for health/readiness, enables native inventory non-interactively, clears the GLPI cache, and exposes `/front/inventory.php` to the VM. The test matrix covers GLPI 10 and GLPI 11 where the selected image tags and bootstrap steps are supported.

The Vagrant environment uses a Windows Server 2022 box over WinRM as the required baseline, with provider blocks for VirtualBox and Hyper-V. Provisioning copies and silently installs the development MSI, verifies the Windows Service, runs local JSON/XML output, sends inventory to GLPI, and exercises repair/upgrade/uninstall behavior. It also runs the root Go executable and the official GLPI Agent portable package when staged, producing a per-category comparison report.

Acceptance validates the JSON against the exact schema from the GLPI container and queries the GLPI API or database for the resulting Computer asset and high-value fields. Comparisons use required fields and documented coverage thresholds rather than strict count equality. Scripts emit machine-readable results and collect logs before cleanup.

Hosted CI runs restore, formatting, analyzers, unit tests, Windows publish, schema fixtures, and MSI build/smoke validation. The full Docker/Vagrant workflow is manual or self-hosted because Windows VMs are large and require nested virtualization/licensing.

### D10: Treat security, licensing, and diagnostics as cross-cutting requirements

Use least-privilege read operations even though the service runs as LocalSystem for complete machine/profile inventory. Validate all configured paths and URLs, prohibit shell interpolation, cap collected sizes, redact credentials/tokens, disable insecure TLS only through an explicit opt-in warning, and protect config/state/log ACLs. Inventory diagnostics must record sources and omissions without logging sensitive registry values or full command lines by default.

Original .NET code remains MIT. Tests can compare outputs and fixtures derived from public Windows APIs, but GPL Perl implementation text is not copied. Third-party package licenses, WiX terms, and official GLPI image licenses are recorded in the SBOM/notices.

## Risks / Trade-offs

- **WMI/CIM queries can hang or ignore cancellation** → select only required properties, set native enumeration timeouts, limit concurrency, dispose query objects, and test timeout behavior with a faulting adapter.
- **Running as LocalSystem changes per-user visibility** → enumerate loaded HKEY_USERS hives and provide explicit, audited `scan-profiles` support for offline hives.
- **Broad Perl parity can delay a usable release** → implement collectors in value-based phases and require the core hardware/OS/network/software set before extended categories.
- **GLPI schemas differ across releases** → test against container-extracted schemas for the supported GLPI matrix and keep transformations version-aware at the protocol boundary.
- **Automatic legacy fallback can mask failures** → downgrade only for explicit protocol incompatibility, never for TLS/authentication/server-health errors.
- **Self-contained output is larger than a Go binary** → accept the size for runtime independence; measure a later single-file/ReadyToRun option without making it a correctness requirement.
- **MSI upgrades can destroy identity or edited configuration** → mark state/config as preserved, test v1-to-v2 upgrades before v1 ships, and keep destructive purge explicit.
- **Elevated installer actions create security risk** → use MSI service/configuration tables, minimize custom actions, validate properties, and test rollback/uninstall paths.
- **WiX 7 terms may be unacceptable for the distributor** → make license acceptance a planning gate and retain installer-source abstraction until the decision is recorded.
- **Docker/Vagrant tests are resource-intensive and provider-dependent** → make them opt-in, support targeted stages, capture artifacts, and keep unit/contract tests runnable without virtualization.
- **Reference agents produce transiently different data** → compare stable identity/high-value fields and minimum coverage, not exact unordered counts.

## Migration Plan

1. Record product naming, WiX licensing, support matrix, and signing decisions before packaging work starts.
2. Scaffold `dotnet-glpi-agent/`, pin .NET/package versions, implement the typed core, config, identity, local output, and fixture adapters.
3. Implement core Windows collectors and native GLPI protocol, then validate local JSON against a container-extracted GLPI schema.
4. Add extended Perl-parity collectors and the optional profile scan, with fixture and Windows integration tests for each category.
5. Add Windows Service hosting and verify start/stop/recovery/cancellation on the Vagrant VM.
6. Author the MSI and test clean install, silent install, repair, upgrade, downgrade blocking, normal uninstall, purge, and rollback before publishing v1.
7. Automate the Docker GLPI 10/11 matrix, GLPI bootstrap, Vagrant provisioning, reference comparison, and asset assertions.
8. Add CI/release artifacts, SBOM/checksums, then enable production signing only after secrets and timestamping are configured.

Rollback is additive: removing `dotnet-glpi-agent/` and its CI entries leaves the Go and Perl trees untouched. On an endpoint, rolling back an agent release uses the previous MSI through the tested major-upgrade path; emergency removal preserves identity/configuration unless `PURGE=1` is explicitly selected.

## Open Questions

- What final product, manufacturer, service, and MSI package identity should be stable before the first public build?
- Does the distributor accept the WiX 7 OSMF EULA and any applicable maintenance fee, or must an alternative MSI toolchain be selected?
- Which certificate provider and timestamp service will sign production executables and MSI packages?
- Is GLPI 11 OAuth2 required for the first release, or can it land immediately after unauthenticated/native GLPI 10/11 inventory?
- Which client editions join Windows Server 2022 in the required validation matrix: Windows 11, Windows Server 2025, or both?
- Should normal uninstall preserve all logs as well as configuration/identity, or retain only configuration and identity?
