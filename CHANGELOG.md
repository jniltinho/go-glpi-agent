# Changelog

All notable changes to this project are documented here. Sections follow
✨ New Features / 🔧 Improvements / 🧹 Cleanup / 📚 Documentation, and the project
follows [Semantic Versioning](https://semver.org/). The version is the git tag;
each release's notes are this file's section for that version (published by CI).

## [Unreleased]

**Windows packaging overhaul (Go + .NET agents).** Both Windows packages now ship
a single binary with `agent.cfg` in the same folder as the executable, and both
run as a real Windows Service.

### ✨ New Features
- feat(windows): the Go agent now installs a **Windows Service** (`go-glpi-agent`,
  auto start, LocalSystem, runs the daemon loop with restart-on-failure) instead
  of an hourly Scheduled Task. The MSI, `install.ps1` and `uninstall.ps1` manage
  the service; upgrades from ≤ 0.5.x remove the legacy Scheduled Task.

### 🔧 Improvements
- feat(windows): `agent.cfg` now lives **next to the binary** for both agents
  (`C:\Program Files\go-glpi-agent\agent.cfg` and
  `C:\Program Files\DotnetGlpiAgent\agent.cfg`); state and logs remain under
  `C:\ProgramData`. Existing configs are preserved on upgrade; `PURGE=1` removes
  them.
- feat(dotnet): the .NET MSI and portable zip now ship a **single self-contained
  executable** (`PublishSingleFile`) instead of the multi-DLL publish layout.
- ci(dotnet): drop the "Schema fixtures present" gate that asserted gitignored,
  lab-generated files and made every CI run fail; assert the single-binary
  publish output instead.

## [0.5.1] — 2026-07-10

**Native GLPI 11 protocol fix (Go + .NET agents).** The native inventory flow now
works against GLPI 11, which rejected it with `400 "JSON not well formed!"`
(CONTACT) and `500 "keys ignored: accountinfo"` (inventory). Fixed and validated
against a live GLPI 11 server.

### 🔧 Improvements
- fix(server): send the native protocol as plain `application/json` — GLPI 11
  does not inflate a zlib CONTACT/inventory body; zlib stays on the legacy
  XML/PROLOG flow. CONTACT now includes the required `version` field.
- fix(server): send the entity `tag` at the envelope root and drop
  `content.accountinfo` (not in the native schema); do not fall back to the
  legacy protocol when the server answers with a native JSON error — surface the
  real GLPI message instead.
- fix(dotnet): mirror the same fixes in the .NET agent — CONTACT `version` and an
  uncompressed native path by default (`Compression = Auto` no longer applies zlib
  to the native flow); regression tests for `tag`-at-root/no-`accountinfo`.
- test(glpi): pin the Go test compose to GLPI 11.0.8 so the native-protocol
  regressions reproduce locally.

### 📚 Documentation
- docs: OpenSpec change `fix-glpi11-native-protocol` documenting the fix across
  the Go (linux/mac/windows) and .NET agents.

## [0.5.0] — 2026-07-10

**Dotnet GLPI Agent (Windows).** Independent .NET 10 Windows inventory agent with
MSI packaging, Windows Service, native GLPI JSON protocol, and E2E validation on
Windows Server 2022 against GLPI 10 and 11.

### ✨ New Features
- feat(dotnet): new `dotnet-glpi-agent/` subproject — self-contained `win-x64`
  agent (`dotnet-glpi-agent.exe`) with typed inventory model, modular collectors
  (OS/BIOS/CPU/memory/storage/network/software/AppX/hotfixes/printers/monitors/
  video/firewall/antivirus/…), native CONTACT/inventory JSON + legacy XML fallback.
- feat(dotnet): Windows Service hosting (`DotnetGlpiAgent`) via Generic Host +
  WiX 4.0.6 per-machine MSI (Program Files + ProgramData, silent properties
  `SERVER`/`TAG`/`INSTALLDIR`/`STARTSERVICE`/`RUNNOW`/`PURGE`, unsigned-dev marker).
- feat(dotnet): Docker GLPI 10/11 lab + Vagrant Windows Server 2022 acceptance
  (schema validation, dual-stack submit, MSI lifecycle, 3-agent comparison).

### 🔧 Improvements
- fix(dotnet): schema-safe datetime (`yyyy-MM-dd HH:mm:ss`); strip illegal XML
  control characters; GLPI CONTACT `expiration` hours; no `content.accountinfo`
  (GLPI 11); TLS custom-CA hostname validation; orchestrator isolation; session
  WMI regex; MSI repair preserves `server`/`tag`; firewall registry fallback;
  per-source WMI degradation; memory synthetic fallback.
- ci: `dotnet.yml` (Linux tests + Windows publish/MSI); monorepo `release.yml`
  builds Go packages **and** Dotnet MSI/portable zip on `v*` tags.

### 📚 Documentation
- docs: `dotnet-glpi-agent/README.md`, MSI/service/operations/parity reports,
  OpenSpec change `add-dotnet10-windows-agent` (122 tasks).

## [0.4.0] — 2026-06-30

**macOS inventory support (Apple Silicon) + a Windows `.msi`.** A single codebase now
builds for Linux, Windows, FreeBSD and macOS. macOS is validated on a `macos-latest`
(arm64) runner with **exact per-section parity** against the official GLPI-Agent, and
the Windows `.msi` is validated with a full install → verify → uninstall round-trip on
`windows-latest`.

### ✨ New Features
- feat(macos): macOS inventory support — `go-glpi-agent` collects the same
  categories on macOS via `gopsutil` plus native sources: `system_profiler -json`
  (`SPHardwareDataType` for model/serial/UUID/boot-ROM, `SPMemoryDataType`,
  `SPNVMeDataType`/`SPSerialATADataType`, `SPUSBDataType`, `SPApplicationsDataType`),
  `sysctl machdep.cpu.*`/`hw.*` (CPU, incl. Apple Silicon chip name), `sw_vers`/`uname`
  (OS), `ioreg` (identity fallback), `networksetup` (interface typing) and `route`.
- feat(macos): system serial/UUID resolved through the official agent's fallback
  chain (`Serial Number` → `Serial Number (system)` → `ioreg IOPlatformSerialNumber`;
  `Hardware UUID` → `ioreg IOPlatformUUID`), with a serial-of-last-resort = UUID rule
  so a Mac is never reported without a serial — including on virtualized CI runners.
- feat(macos): Apple Silicon distribution — `make build-macos`/`package-macos` produce
  the `darwin/arm64` binary and a `.pkg` + `.dmg` installer (`pkgbuild`/`productbuild`
  + `hdiutil`) with a `LaunchDaemon` for scheduled runs; `contrib/macos/` holds the
  daemon, pre/postinstall, `uninstall.sh` and build driver. (Intel builds from source.)
- feat(windows): Windows `.msi` installer for managed deployment (GPO / Intune /
  SCCM / PDQ). Installs the `.exe`, registers the hourly `go-glpi-agent` Scheduled
  Task (SYSTEM), seeds `agent.cfg`, supports silent install with `SERVER`/`TAG`
  properties (`msiexec /i … /qn SERVER=… TAG=…`), in-place upgrades (stable
  `UpgradeCode`), and clean uninstall by product code (`PURGE=1` to also drop
  config). Built **on Linux with `wixl`** (msitools) — no Windows build host — via
  `make package-msi`; a `contrib/windows/msi/Dockerfile` builds it in a container.
- feat(windows): hidden `service install|uninstall|configure|purge` subcommands the
  MSI's deferred (SYSTEM) custom actions call; the exe self-locates via
  `os.Executable()` and owns `agent.cfg` (writes a default only when absent, so
  upgrades preserve operator edits).

### 🔧 Improvements
- fix(agent): when the configured `vardir` is not writable (e.g. the agent is run
  manually, not installed under the system prefix), fall back to a per-user cache
  directory so the `deviceid`/`agentid` still persist across runs instead of being
  regenerated — and the noisy mkdir-permission warning is gone.
- ci(macos): new `macos.yml` (arm64 `macos-latest`) builds, runs a real inventory,
  validates the native JSON against GLPI's schema, installs and runs the official
  GLPI-Agent for a per-section comparison, asserts the serial is never empty, and
  uploads the `.pkg`/`.dmg`; `release.yml` publishes the Apple Silicon installers;
  `go.yml` adds `darwin/amd64`+`arm64` compile/vet checks.
- ci(windows-msi): new workflow builds the `.msi` on Ubuntu (`wixl`) and runs a full
  install → verify (binary + Scheduled Task + configured `agent.cfg`) → inventory +
  schema-validate → uninstall round-trip on `windows-latest`; `release.yml` publishes
  the `.msi` alongside the existing artifacts.

## [0.3.0] — 2026-06-30

**FreeBSD inventory support + VirtualBox serial parity.** A single codebase now
builds for Linux, Windows and FreeBSD. Validated end-to-end on FreeBSD 14.1 against
a real GLPI 10 (native JSON schema-valid, asset created), and the Linux build
cross-checked on Debian 12 against the official glpi-agent (softwares 455/455).

### ✨ New Features
- feat(freebsd): FreeBSD inventory support — `go-glpi-agent` collects the same
  categories on FreeBSD via `gopsutil` plus native sources: `kenv smbios.*`
  (BIOS/board/chassis/UUID), `pkg query` (software), `geom`/`camcontrol` (disks),
  sysctl (CPU/OS), `/var/db/zoneinfo` (timezone) and `usbconfig` (USB).
- feat(freebsd): FreeBSD distribution — `make build-freebsd`/`package-freebsd`
  produce a `.tar.gz` (binary + `agent.cfg` + `rc.d` service + `INSTALL.md`);
  `release.yml` publishes it.

### 🔧 Improvements
- fix(bios): on VirtualBox VMs, where the DMI/SMBIOS serial is `0`, fall back to the
  system UUID as the serial (matching glpi-agent's `Generic/Dmidecode/Bios.pm`), so
  the host gets a stable identity in GLPI instead of an empty serial. Applies to
  Linux, Windows and FreeBSD.
- refactor: per-OS registration extended with `register_freebsd.go`; cross-platform
  `generic` timezone gains a FreeBSD source (`/var/db/zoneinfo`).
- ci: `go.yml` adds a `GOOS=freebsd` build/vet check.

### 📚 Documentation
- docs: README "FreeBSD" section + a FreeBSD column in the per-OS collector table;
  AGENTS.md per-OS layout; `test/vagrant-freebsd/` end-to-end validation comparing
  against the official `p5-FusionInventory-Agent`.

## [0.2.0] — 2026-06-30

**Windows inventory support.** A single codebase now builds for Linux and Windows;
`go-glpi-agent.exe` collects the same categories via WMI and the registry and sends
them to GLPI 10+. Validated end-to-end on Windows Server 2022 against a real GLPI 10
(native JSON schema-valid, computer asset created over both WinRM and SSH) and
cross-checked against the official glpi-agent 1.18.

### ✨ New Features
- feat(windows): Windows inventory support — `go-glpi-agent.exe` collects the same
  categories as the Linux build (OS, CPU, memory + slots, BIOS/board/chassis, disks,
  filesystems, USB, network, software, users, timezone, processes) via `gopsutil`,
  WMI (`Win32_*`) and the registry, and sends them to GLPI 10+.
- feat(windows): Windows distribution — `make build-windows`/`package-windows`
  produce a `.zip` (exe + `agent.cfg` + `install.ps1`/`uninstall.ps1`) built on the
  Linux CI runner; `install.ps1` registers an hourly Scheduled Task (the analog of
  the systemd timer). Software is read from the uninstall registry keys (not the
  slow, side-effecting `Win32_Product`).

### 🔧 Improvements
- refactor: split the codebase per-OS with build tags (`collector/linux`,
  `collector/windows`, cross-platform `collector/generic`); register collectors via
  `internal/agent/register_<goos>.go` so adding macOS/BSD is a sibling package + one file.
- refactor: OS-split logger (`logger_unix.go` syslog vs `logger_windows.go` stub) so
  `GOOS=windows go build` compiles; OS-aware default paths (`%ProgramData%` on Windows).
- refactor: share the DMI/WMI junk-value filter as `sysutil.CleanDMI`.
- ci: `go.yml` adds a `GOOS=windows` build/vet check and a `windows-latest` job that
  runs the agent on real Windows and validates the native JSON against GLPI's
  `inventory.schema.json`; `release.yml` publishes the Windows `.zip`.

### 📚 Documentation
- docs: README "Windows" section + per-OS collector table; AGENTS.md per-OS layout
  and WMI/registry conventions; `test/vagrant-windows/` for end-to-end validation
  (WinRM + SSH) comparing against the official glpi-agent.

## [0.1.3] — 2026-06-30

### 📚 Documentation
- docs: godoc comments on every function/method, type, and package (per the
  golang-documentation standard) — `go doc` now renders the full API.
- docs: add `CONTRIBUTING.md` (build/test/PR flow) and `llms.txt` (structured
  overview for AI agents).

## [0.1.2] — 2026-06-30

### 📚 Documentation
- docs: polish the `create-release` skill — adopt the clearer structure
  (release-approaches table, commit→section categorization, monitor/verify
  checklists, workflow-capabilities and adapting tables) from the go-postfixadmin
  skill, while keeping this project's CHANGELOG-driven CI publishing, nfpm
  `.deb`/`.rpm`/Arch packaging, and YAML frontmatter for skill discovery.

## [0.1.1] — 2026-06-30

### 🔧 Improvements
- ci: bump GitHub Actions to Node 24 (`actions/checkout@v5`, `actions/setup-go@v6`)
  and publish releases with the native `gh` CLI, removing the last Node 20 action.
- ci: release notes are written from `CHANGELOG.md` (`--notes-file`) instead of
  being auto-generated from commits.

## [0.1.0] — 2026-06-29

First release: a Go reimplementation of the FusionInventory/GLPI inventory agent
for Linux, distinct from the Perl `fusioninventory-agent` and the official
`glpi-agent` so the three can coexist.

### ✨ New Features
- feat: **GLPI 10+ native protocol** (primary) — CONTACT probe, JSON inventory to
  `/front/inventory.php`, `GLPI-Agent-ID` (UUID v4) header, zlib compression (or
  none). Validated against a real GLPI 10 with zero `inventory.schema.json`
  violations.
- feat: automatic **legacy XML/PROLOG fallback** when the server is not native.
- feat: **Linux collectors** — CPU, memory (+ dmidecode slots), BIOS/DMI, physical
  disks (lsblk), filesystems, LVM, USB, network, OS/distro, hostname, timezone,
  users/groups/logged-in, processes, and software (dpkg/rpm/pacman).
- feat: **Cobra CLI** with `run`, `daemon`, and `version` subcommands.
- feat: reads the Perl agent's `agent.cfg` (INI), installed at
  `/opt/go-glpi-agent/agent.cfg`.
- feat: **systemd** — oneshot `.service` + hourly `.timer`, plus an optional daemon
  unit; everything installs under `/opt/go-glpi-agent`.
- feat: **packaging** — `.deb`, `.rpm`, Arch `.pkg.tar.zst` (nfpm) and a `.tar.gz`,
  plus a GitHub Actions release workflow on `v*` tags.
- feat: persistent device ID in the Perl format and a separate `agentid` UUID;
  imports an existing `FusionInventory-Agent.dump` / `GLPI-Agent.dump` on first run.

### 🔧 Improvements
- DMI junk-value filtering (serials of `0`, `None`, `To be filled by O.E.M.`, …),
  so meaningless values are not reported as real data.
- Validated across 16 Linux distributions (RHEL/Rocky/Alma/Oracle 8–9, CentOS
  Stream 10, Fedora 42, Debian 12/13, Ubuntu 24.04/26.04, Pop!_OS 20.04,
  openSUSE Leap 15, Arch Linux).
