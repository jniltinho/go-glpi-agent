# Changelog

All notable changes to this project are documented here. Sections follow
✨ New Features / 🔧 Improvements / 🧹 Cleanup / 📚 Documentation, and the project
follows [Semantic Versioning](https://semver.org/). The version is the git tag;
each release's notes are this file's section for that version (published by CI).

## [Unreleased]

—

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
