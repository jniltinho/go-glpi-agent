## 1. Product decisions and isolated scaffold

- [x] 1.1 Record the final product/manufacturer/service names, Windows support matrix, production signing owner, and whether GLPI 11 OAuth2 is required for v1.
- [x] 1.2 Record acceptance or rejection of the WiX Toolset 7 OSMF EULA and applicable fee; if rejected, select and document an approved MSI toolchain before packaging tasks begin.
- [x] 1.3 Create the independent `dotnet-glpi-agent/` tree with `src`, `tests`, `packaging/wix`, `test/glpi`, `test/vagrant-windows`, and `docs` directories.
- [x] 1.4 Create `DotnetGlpiAgent.sln` and the Core, Windows, Protocol, App, and four test projects with the dependency direction defined in design D1.
- [x] 1.5 Pin the .NET 10 SDK in `global.json` and centralize package versions in `Directory.Packages.props`.
- [x] 1.6 Add shared build settings for nullable reference types, warnings as errors, deterministic builds, embedded source metadata, analyzers, formatting, and `win-x64` self-contained publish without trimming/AOT.
- [x] 1.7 Add project-local MIT licensing, third-party notice policy, clean-room reference guidance, `.gitignore`, README, and build/test commands.
- [x] 1.8 Verify restore, build, test discovery, and `win-x64` publish from inside `dotnet-glpi-agent/` without invoking or modifying Go/Perl projects.

## 2. Domain model, configuration, identity, and diagnostics

- [x] 2.1 Define typed inventory envelope, identity, account/tag, collection metadata, and core OS/hardware/BIOS/CPU/memory records in Core.
- [x] 2.2 Define typed storage, drive, network, USB/PnP, software, user/group/session, and process records.
- [x] 2.3 Define typed extended records for hotfix/AppX, printer, monitor, controller, video, battery, sound/input/port, antivirus, and firewall data.
- [x] 2.4 Implement an inventory snapshot assembler with category-specific merge keys, deterministic ordering, and collection completeness metadata.
- [x] 2.5 Implement shared normalization for placeholder DMI values, dates/times, architectures, identifiers, booleans, units, strings, and stable sort keys.
- [x] 2.6 Implement `agent.cfg` parsing with include support, defaults, source tracking, unknown-key warnings, and fixture tests compatible with the selected Perl/Go keys.
- [x] 2.7 Implement configuration precedence for file, `GLPI_AGENT_*` environment variables, and CLI options plus semantic validation and redacted effective-config reporting.
- [x] 2.8 Implement Windows default paths for Program Files and ProgramData and validate that executable/config/include paths do not trust unprivileged writable locations.
- [x] 2.9 Implement atomic cryptographic agent/device ID creation, persistence, corruption handling, and restart tests.
- [x] 2.10 Implement explicit identity import for root Go state and documented migration input without executing or directly deserializing untrusted Perl dumps.
- [x] 2.11 Implement structured correlation scopes, secret redaction, Event Log abstraction, protected rolling file logging, and retention tests.

## 3. Collector framework and command surface

- [x] 3.1 Define `IInventoryCollector`, typed contribution/result, support state, category, deadline, and source-diagnostic contracts.
- [x] 3.2 Implement bounded concurrent orchestration with per-collector linked cancellation, native-timeout enforcement, partial-inventory policy, and deterministic result assembly.
- [x] 3.3 Add fake success, access-denied, malformed, timeout, non-cancellable, and cancellation collectors to verify isolation and resource observation.
- [x] 3.4 Implement the App composition root and dependency injection registrations without Windows API access leaking into Core or Protocol.
- [x] 3.5 Implement `version` and `validate-config` commands with documented stdout/stderr behavior and exit codes.
- [x] 3.6 Implement the one-shot `run` command with local/server targets, force, category exclusions, debug, cancellation, and categorized exit codes.
- [x] 3.7 Implement atomic deterministic local JSON and XML output and tests proving both formats consume one snapshot.

## 4. Windows query adapters and core identity collectors

- [x] 4.1 Implement injectable WMI/CIM query adapters with selected-property queries, native enumeration timeouts, cancellation, COM disposal, and categorized errors.
- [x] 4.2 Implement injectable registry adapters with explicit 32/64-bit views, safe value conversion, handle disposal, and fixture capture/replay support.
- [x] 4.3 Implement BCL adapters for host, time, network, filesystem, and process data with bounded enumeration.
- [x] 4.4 Create sanitized Windows fixture sets and tests for normal hardware, VirtualBox placeholders, Server Core omissions, access denial, malformed values, and timeouts.
- [x] 4.5 Implement the OS collector for name, edition, display version, build/UBR, kernel, architecture, hostname/domain, boot/install time, and timezone.
- [x] 4.6 Implement BIOS/baseboard/chassis/system UUID/asset tag collection with cleaned identity fallback rules.
- [x] 4.7 Implement CPU/socket/core/thread/speed/manufacturer/architecture collection and canonical mappings.
- [x] 4.8 Implement total/swap memory and physical memory-slot collection with capacity, speed, type, serial, and empty-slot handling.
- [x] 4.9 Add unit and fixture tests for every core identity collector and normalization edge case.

## 5. Storage, network, devices, users, and processes

- [x] 5.1 Implement physical disk and storage-controller collection with model, serial, firmware, capacity, interface, media type, and NVMe/SSD/HDD classification.
- [x] 5.2 Implement logical volume/filesystem collection with drive type, filesystem, label, mount/letter, total/free space, and system-drive indication.
- [x] 5.3 Implement network collection using BCL interfaces enriched with WMI/CIM gateway, DNS, DHCP, adapter type, speed, status, and virtual classification.
- [x] 5.4 Emit one deterministic network entry per relevant address plus an address-less adapter fallback and deduplicate joined BCL/WMI data.
- [x] 5.5 Implement USB and PnP collection with VID/PID/serial parsing, device classification, connected-state filtering, and hub suppression rules.
- [x] 5.6 Implement local user/group and active/logged-on session collection with stable Windows identifiers and local/domain distinction.
- [x] 5.7 Implement the opt-in process collector with PID, owner, command, start time, memory, redaction/size caps, and access-denied degradation.
- [x] 5.8 Add unit and Windows fixture tests for storage, volume, network, USB/PnP, user/group/session, and process collectors.

## 6. Software and update inventory

- [x] 6.1 Implement HKLM uninstall enumeration for native and WOW64 registry views without using `Win32_Product`.
- [x] 6.2 Map uninstall records to typed software with name, version, publisher, architecture, install date, size, URL/uninstall metadata, and system classification.
- [x] 6.3 Implement loaded HKEY_USERS software enumeration with user SID/name attribution and deterministic source precedence.
- [x] 6.4 Implement opt-in offline profile discovery/hive loading with privilege checks, strict path validation, `finally` unload, and fault-injection cleanup tests.
- [x] 6.5 Implement AppX package collection through reviewed Windows package APIs without shell interpolation.
- [x] 6.6 Implement hotfix/update collection and classify security updates, hotfixes, and ordinary updates.
- [x] 6.7 Implement software deduplication across machine, user, AppX, and hotfix sources while preserving meaningful architecture/user distinctions.
- [x] 6.8 Add fixtures and table-driven tests for 32/64-bit views, malformed dates/versions, missing names, system components, duplicate rows, user profiles, AppX, and hotfixes.

## 7. Extended Perl-parity collectors

- [x] 7.1 Implement printer collection with local/network/shared/default/status/driver/port fields.
- [x] 7.2 Implement monitor collection using WMI monitor identity and EDID/registry fallback with sanitized manufacturer/model/serial.
- [x] 7.3 Implement video, storage/display controller, sound, input, modem, and port collectors with stable PnP identities.
- [x] 7.4 Implement battery collection with design/full/current capacity, chemistry, voltage, status, and absent-battery behavior.
- [x] 7.5 Implement antivirus collection across SecurityCenter2 when present and Microsoft Defender namespaces/services on server editions.
- [x] 7.6 Implement firewall profile collection through reviewed Windows management APIs with domain/private/public status.
- [x] 7.7 Add support-state and fixture tests proving absent desktop-only namespaces do not fail Server Core inventories.
- [x] 7.8 Produce and document a collector coverage matrix mapping each .NET category/field to Windows source and Perl/Go reference behavior.

## 8. GLPI native and legacy protocols

- [x] 8.1 Implement one shared mapping from the typed snapshot to protocol content used by both native JSON and legacy XML serializers.
- [x] 8.2 Implement canonical native inventory JSON envelope, typed normalization, required-field cleanup, stable ordering, and golden tests.
- [x] 8.3 Implement CONTACT JSON with agent/version/device/tag/inventory-task fields and validate native server answers.
- [x] 8.4 Implement persistent `GLPI-Agent-ID`, user-agent, and request/correlation header handling with preflight identity validation.
- [x] 8.5 Implement bounded `pending` polling using `GLPI-Request-ID`, server expiration, cancellation, poll/time caps, and no-body GET semantics.
- [x] 8.6 Implement legacy PROLOG and OCS/FusionInventory XML serialization from the shared content mapping.
- [x] 8.7 Implement the HTTP client with proxy, basic authentication, timeouts, system/custom CA trust, optional client certificates, zlib/gzip/no compression, and redacted diagnostics.
- [x] 8.8 Implement optional OAuth2 client-credentials token acquisition/refresh for the accepted GLPI 11 scope decision.
- [x] 8.9 Implement conservative protocol fallback that downgrades only on explicit legacy incompatibility and never on TLS/auth/rate-limit/timeout/server-health failures.
- [x] 8.10 Implement bounded retry and indeterminate-submission handling that cannot blindly duplicate an inventory upload.
- [x] 8.11 Add mock-server contract tests for native success, pending, schema error, authentication, TLS failure, legacy fallback, compression, cancellation, and ambiguous upload completion.
- [x] 8.12 Add a schema-validation test utility that consumes an externally supplied GLPI `inventory.schema.json` and reports exact JSON paths.

## 9. Windows Service hosting

- [x] 9.1 Add .NET Windows Service hosting to the App project with the same composition root and executable used by CLI commands.
- [x] 9.2 Implement randomized initial delay, post-completion interval scheduling, single-cycle locking, and service status reporting.
- [x] 9.3 Propagate SCM stop/shutdown cancellation through scheduling, collection, serialization, and transport with a bounded stop grace period.
- [x] 9.4 Route service lifecycle/high-severity messages to Event Log and detailed redacted diagnostics to protected rolling files.
- [x] 9.5 Add host-level tests for no-overlap scheduling, expected-error survival, cancellation during collection/submission, and unexpected process failure behavior.
- [x] 9.6 Document service account permissions and verify the service loads executable/configuration only from protected locations.

## 10. MSI packaging and lifecycle

- [x] 10.1 Create the approved SDK-style MSI packaging project and wire version/manufacturer/product/service identity from centralized build properties.
- [x] 10.2 Package the complete self-contained publish output under Program Files with deterministic components and protected ProgramData directories.
- [x] 10.3 Author first-install `agent.cfg` seeding and ACLs so administrators/SYSTEM can manage secrets while upgrades/repairs preserve edits.
- [x] 10.4 Author declarative service install/control, delayed automatic start, dependencies, description, failure recovery, and safe quoted command line.
- [x] 10.5 Add documented non-secret silent properties (`SERVER`, `TAG`, `INSTALLDIR`, `STARTSERVICE`, `RUNNOW`) with validation and logging tests.
- [x] 10.6 Implement a protected post-install secret provisioning workflow and document why ordinary MSI command-line secrets are rejected.
- [x] 10.7 Author stable major-upgrade identity, generated product versions, downgrade blocking, and configuration/identity preservation.
- [x] 10.8 Author normal uninstall preservation and explicit elevated `PURGE=1` removal with a clear destructive warning.
- [x] 10.9 Review every elevated custom action; replace it with MSI tables where possible and add validated conditions plus rollback for any retained action.
- [x] 10.10 Build automated clean install, silent install, service, repair, v1-to-v2 upgrade, downgrade, uninstall/reinstall, purge, and faulted rollback tests.
- [x] 10.11 Add versioned MSI output, SHA-256 checksums, SBOM, third-party notices, unsigned-development labeling, and production Authenticode/timestamp hooks.

## 11. Dockerized GLPI laboratory

- [x] 11.1 Add project-local Docker Compose definitions and environment templates using pinned official `glpi/glpi` and MariaDB image versions.
- [x] 11.2 Add GLPI/database health checks, isolated named volumes, host port configuration, and digest capture.
- [x] 11.3 Automate first-start readiness, native inventory enablement, and cache clearing for the pinned GLPI 10 stack.
- [x] 11.4 Add an isolated pinned GLPI 11 configuration and automate any version-specific bootstrap/API enablement required by native inventory/OAuth2.
- [x] 11.5 Implement commands to start, wait, inspect, reset, and stop one GLPI version or the full matrix without touching the root Go test stack.
- [x] 11.6 Implement extraction of the active container's exact `inventory.schema.json` into versioned test artifacts.
- [x] 11.7 Document GLPI endpoint addressing from VirtualBox and Hyper-V guests and validate host/VM connectivity before provisioning.

## 12. Vagrant Windows end-to-end tests

- [x] 12.1 Add a Windows Server 2022 Vagrantfile using WinRM with configurable VirtualBox/Hyper-V resources and GLPI endpoint/version selection.
- [x] 12.2 Add file provisioners for the development MSI, previous-version MSI fixture, test configuration, and optional official Perl/root Go reference artifacts.
- [x] 12.3 Implement PowerShell provisioning for silent MSI installation, ACL/config inspection, service start/status, and local JSON/XML collection.
- [x] 12.4 Validate local JSON against the schema extracted from the active GLPI container and fail with precise paths.
- [x] 12.5 Submit native inventory, capture correlation logs, and assert exactly one matching GLPI Computer plus core OS/BIOS/CPU/memory/storage/network/software fields through API or test database.
- [x] 12.6 Run optional official GLPI Agent and root Go agent reference inventories on the same VM and emit stable-field/count/omission comparison JSON.
- [x] 12.7 Implement the MSI lifecycle suite for repair, upgrade, downgrade blocking, uninstall preservation, reinstall identity, purge, and injected rollback.
- [x] 12.8 Collect application logs, Event Log entries, MSI logs, inventories, schemas, image digests, service state, and JUnit/machine-readable summaries.
- [x] 12.9 Add an orchestration script with selectable stages, `KeepResources` debugging, bounded waits, reliable error propagation, and default ephemeral cleanup.
- [x] 12.10 Document host prerequisites, Windows evaluation licensing, network/provider caveats, expected duration/storage, troubleshooting, rerun, and destroy commands.

## 13. CI, security, and release automation

- [x] 13.1 Add hosted CI jobs for restore, formatting, analyzers, license checks, unit/contract tests, fixture schema validation, and deterministic builds.
- [x] 13.2 Add a Windows CI job for self-contained `win-x64` publish, MSI build, artifact inspection, and non-virtualized packaging smoke tests.
- [x] 13.3 Add dependency vulnerability scanning, secret scanning, SBOM generation, and checks that logs/test artifacts contain no configured credentials.
- [x] 13.4 Add a manual or self-hosted Docker/Vagrant workflow with explicit virtualization/licensing prerequisites and GLPI 10/11 selection.
- [x] 13.5 Add release automation for version propagation, MSI/portable artifacts if enabled, checksums, notices, SBOM, signature verification, and retention.
- [x] 13.6 Configure production signing/timestamp secrets only after the recorded signing decision and verify that untrusted pull requests cannot access them.
- [x] 13.7 Document supported Windows/GLPI versions, CLI/configuration, service operations, silent MSI deployment, upgrades/uninstall/purge, and test-lab use.

## 14. Final acceptance

- [x] 14.1 Run all unit, contract, formatting, analyzer, vulnerability, and deterministic-build checks with no unexplained warnings.
- [x] 14.2 Verify the self-contained publish runs on a clean Windows Server 2022 VM without a separately installed .NET runtime.
- [x] 14.3 Run every core and extended collector on the Vagrant baseline and review the documented coverage/omission report.
- [x] 14.4 Validate representative native inventories against the exact pinned GLPI 10 and GLPI 11 schemas and complete real submissions to both stacks.
- [x] 14.5 Verify GLPI creates one correctly identified Computer with required high-value fields and no duplicate asset on service restart, MSI repair, or upgrade.
- [x] 14.6 Complete the full MSI lifecycle matrix, including rollback and identity/configuration preservation, before the first production MSI is approved.
- [x] 14.7 Review the three-agent comparison report and resolve every unexplained regression below the agreed coverage thresholds.
- [x] 14.8 Complete security, ACL, redaction, custom-action, dependency-license, WiX-license, SBOM, and signing reviews.
- [x] 14.9 Update project README, architecture/collector/protocol/MSI/test documentation, root navigation, and changelog with validated commands and known limitations.
- [x] 14.10 Record final artifact hashes, image digests, test reports, supported matrix, and release sign-off; leave the OpenSpec change ready for archive.
