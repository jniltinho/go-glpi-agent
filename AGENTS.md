# AGENTS.md

Guidance for AI agents (and humans) working on **go-glpi-agent**.

## What this is

A Go reimplementation of the FusionInventory/GLPI inventory agent for **Linux,
Windows, FreeBSD and macOS** (Intel + Apple Silicon). It produces a single static binary (`go-glpi-agent` / `.exe`)
that collects local hardware/software inventory and sends it to a **GLPI 10+** server
using the native JSON protocol, with automatic fallback to the legacy
OCS/FusionInventory XML protocol. It can also write the inventory to a local XML file.

The Go agent lives at the repository root. The Perl reference projects (kept
intact, for behavior comparison only) live under `base/` (`base/perl/`,
`base/glpi-agent/`).

## Conventions (follow these)

- **English only.** All Go code — comments, doc comments, log/error messages,
  identifiers — is written in English. Do not introduce Portuguese into the code.
- **Cobra CLI layout**, matching the other Go projects in this account
  (e.g. `go-manager-server`, `go-rundeck`):
  - `main.go` at the root is a thin entrypoint that calls `cmd.Execute()`.
  - `cmd/root.go` defines the root command and shared/persistent flags.
  - `cmd/<name>.go` defines one subcommand each (`run`, `daemon`, `version`).
  - Business logic stays in `internal/...`, never in `cmd/`.
- **Module path:** `go-glpi-agent` (no host prefix). The GitHub
  remote is `github.com/jniltinho/go-glpi-agent`.
- **Stdlib first, then existing deps.** Runtime dependencies are `gopsutil`
  (collection), `cobra` (CLI), `yusufpapurcu/wmi` + `x/sys/windows/registry`
  (Windows collection); inventory IDs use `crypto/rand` (no UUID dependency).
  Don't add a dependency for what a few lines can do.
- Keep the data model in `internal/inventory` as the single source of truth;
  the XML and JSON serializers both read from it (see `internal/transport/server`).

## Per-OS layout (build tags)

The code is split by OS so each platform's binary carries only its own collectors,
and adding macOS/BSD later is "drop in a package + a registration file":

- `internal/collector/linux/` — `//go:build linux`, reads `/sys`, `/proc`, dmidecode, lsblk, lvs.
- `internal/collector/windows/` — `//go:build windows`, reads WMI (`Win32_*`) and the registry.
  Pure parsing helpers live in `windows/parse.go` (**no** build tag, no Windows-only
  imports) so they are unit-tested on any platform (`windows/parse_test.go`).
- `internal/collector/freebsd/` — `//go:build freebsd`, reads kenv (`smbios.*`), `pkg`,
  `geom`/`camcontrol`, sysctl and `usbconfig`. Pure parsers in `freebsd/parse.go`
  (no build tag) are unit-tested on any platform (`freebsd/parse_test.go`).
- `internal/collector/macos/` — `//go:build darwin`, reads `system_profiler -json`
  (`SPHardwareDataType`, `SPMemoryDataType`, `SPNVMeDataType`/`SPSerialATADataType`,
  `SPUSBDataType`, `SPApplicationsDataType`), `sysctl machdep.cpu.*`/`hw.*`, `sw_vers`,
  `ioreg` (identity fallback) and `route`. Pure parsers + the serial/UUID
  `resolveIdentity` logic live in `macos/parse.go` (no build tag) and are unit-tested
  on any platform (`macos/parse_test.go`); the command I/O lives in `macos/sysprofiler.go`
  (`//go:build darwin`). Builds for both `darwin/amd64` (Intel) and `darwin/arm64`
  (Apple Silicon). Serial resolves through `Serial Number` → `Serial Number (system)` →
  `ioreg IOPlatformSerialNumber`, then falls back to the UUID so a host is never
  serial-less (important on virtualized CI runners).
- `internal/collector/generic/` — cross-platform; `users.go` is `//go:build !windows`,
  and timezone has a FreeBSD source (`timezone_freebsd.go`, reads `/var/db/zoneinfo`).
- Registration: `internal/agent/register_<goos>.go` blank-imports that OS's package.
- `internal/logger/logger_unix.go` (syslog) vs `logger_windows.go` (stub); OS-aware
  default paths in `internal/config/paths_<unix|windows>.go`.

Conventions for Windows collectors: gate `IsEnabled` on `runtime.GOOS == "windows"`;
run identity strings through `sysutil.CleanDMI`; **never** use WMI `Win32_Product`
(slow, triggers MSI self-repair) — read installed software from the uninstall
registry keys. Verify changes with `GOOS=windows go build ./...` and `GOOS=windows go vet ./...`.

## Go skills

This repo ships curated Go skills under `.agents/skills/`. Consult them when
relevant instead of guessing conventions:

- `golang-spf13-cobra`, `golang-spf13-viper` — CLI/config patterns
- `golang-code-style`, `golang-naming`, `golang-documentation` — style
- `golang-error-handling`, `golang-structs-interfaces`, `golang-concurrency`
- `golang-testing`, `golang-lint`, `golang-modernize`, `golang-security`

## Planning workflow

Specs and tasks live in `openspec/changes/`. Use the OpenSpec skills
(`openspec-apply-change`, `openspec-propose`, etc.) to drive implementation; keep
`tasks.md` checkboxes up to date as work lands.

## Build, test, run

```sh
make build           # local binary ./go-glpi-agent
make build-all       # static linux/amd64 in dist/
make build-windows   # static windows/amd64 (dist/go-glpi-agent.exe)
make package-windows # Windows .zip (exe + agent.cfg + install/uninstall.ps1)
make test            # go test ./...
go vet ./...
CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build ./...   # Windows compile check
GOOS=windows go vet ./...
CGO_ENABLED=0 GOOS=freebsd GOARCH=amd64 go build ./...   # FreeBSD compile check
GOOS=freebsd go vet ./...
make build-freebsd   # static freebsd/amd64; package-freebsd → tarball + rc.d
CGO_ENABLED=0 GOOS=darwin GOARCH=amd64 go build ./...     # macOS compile check (Intel)
CGO_ENABLED=0 GOOS=darwin GOARCH=arm64 go build ./...     # macOS compile check (Apple Silicon)
make build-macos     # static darwin/amd64 + darwin/arm64
make package-macos ARCH=arm64   # .pkg + .dmg (REQUIRES macOS: pkgbuild/hdiutil)

./go-glpi-agent run --local /tmp/inv          # write XML locally
./go-glpi-agent run --server http://glpi/front/inventory.php
./go-glpi-agent daemon                         # periodic cycles
./go-glpi-agent version
```

The agent reads the same `agent.cfg` (INI) as the Perl agent; CLI flags override
the file.

## Test infrastructure

`test/` holds integration infra to run **on a test host**, not the dev machine:

- `test/glpi/` — GLPI 10 + MariaDB via `docker compose`. Enable native
  inventory before testing: set `glpi_configs.enabled_inventory = 1`
  (context `inventory`) and clear the GLPI cache (`php bin/console cache:clear`).
- `test/vagrant/` — a multi-distro VirtualBox matrix (RHEL/Rocky/Alma/Oracle/
  CentOS/Fedora, Debian/Ubuntu/Pop!_OS, openSUSE, Arch). Run `make build-all`
  first; `dist/go-glpi-agent` is copied into each VM via a file provisioner
  (works on boxes without guest additions). `make fetch-glpi-agent` adds the
  official glpi-agent AppImage as a reference.
- `test/vagrant-windows/` — a Windows Server 2022 box (WinRM-provisioned). Run
  `make build-windows` first; `provision.ps1` installs the agent and sends a
  native inventory to the GLPI stack in `test/glpi/`.
- `test/vagrant-freebsd/` — a FreeBSD 14 box. Run `make build-freebsd` first;
  `provision.sh` installs the agent, compares with the official
  `p5-FusionInventory-Agent` (`pkg`), and sends to the GLPI stack.

When validating the native protocol, GLPI's strict `inventory.schema.json`
(in the GLPI container under `vendor/glpi-project/inventory_format/`) is the
source of truth for field types/enums — the JSON serializer normalizes dates,
arch, and a few typed fields to satisfy it.
