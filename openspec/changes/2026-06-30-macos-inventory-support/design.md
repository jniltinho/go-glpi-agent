# Design — macOS inventory support

## Context

macOS is the fourth target after Linux, Windows and FreeBSD. The per-OS plugin
architecture is already in place, so this change is additive: a build-tagged
`internal/collector/macos/` package, a `register_darwin.go`, and the build/packaging/CI
plumbing. The agent is a single static Go binary, so — unlike the official glpi-agent,
which builds Perl + OpenSSL + CPAN from source in `contrib/macosx/glpi-agent-packaging.sh`
— packaging is just `pkgbuild`/`productbuild` + `hdiutil`, fast enough to run entirely
in GitHub Actions. **GitHub Actions is the only validation environment** (no local Mac).

## Goals / Non-goals

**Goals**
- Build-tagged `macos` collectors filling the same inventory model the other OSes fill.
- Reliable **system identity**: never emit an inventory without a serial when any
  stable identifier (serial *or* UUID) is obtainable — including on CI VMs.
- Dual-arch binaries (`amd64` Intel + `arm64` Apple Silicon) and four installers
  (`x86_64`/`arm64` × `.pkg`/`.dmg`), matching the official artifact naming.
- A macOS CI job that builds, runs a real inventory, schema-validates the native JSON,
  and **compares output against the official GLPI-Agent installed from its release**.

**Non-goals**
- Code-signing / notarization (needs a paid Apple Developer ID + secrets) — follow-up.
- A Homebrew tap; MDM-profile / antivirus / firewall / battery sub-inventories.
- CGO-dependent data sources; everything is via stdlib, gopsutil, or shelling out.

## Data sources (grounded in the official MacOS collectors)

| Section | Primary source | Fallback / notes |
|---|---|---|
| OS / kernel | `sw_vers` (ProductName/Version/BuildVersion), `uname -r`/`-m`, gopsutil/host | Kernel name = `Darwin`; boot time from gopsutil/host |
| CPU | `sysctl machdep.cpu.brand_string`/`hw.physicalcpu`/`hw.logicalcpu`, gopsutil/cpu | Apple Silicon has **no** `machdep.cpu.brand_string` → use `system_profiler SPHardwareDataType` "Chip" + `hw.model` |
| Memory | gopsutil/mem (total/swap); `SPMemoryDataType` for slots | Apple Silicon RAM is soldered/unified → one logical entry, not per-DIMM |
| System identity (BIOS) | `system_profiler SPHardwareDataType` (Model, Serial, UUID, Boot ROM) | **`ioreg -c IOPlatformExpertDevice`** fallback (see below) |
| Storage (disks) | `system_profiler SPNVMeDataType` + `SPSerialATADataType` | Filesystems via gopsutil/disk (skip pseudo mounts) |
| Network | gopsutil/net; gateway via `route -n get default` | Type (wifi/ethernet/loopback) from name + flags |
| USB | `system_profiler SPUSBDataType` | Skip hubs |
| Software | `system_profiler SPApplicationsDataType` | `FROM = system_profiler`; name/version/publisher/install-date |

All `system_profiler` calls use `-json` (or `-xml`) and the typed parser lives in a
**build-tag-free `parse.go`** (no darwin-only calls), unit-tested on Linux CI, mirroring
the windows/freebsd packages.

## Serial & UUID — the fallback chain (explicit, per the official agent)

This is the trickiest part and the reason CI inventories can otherwise come back
**without a serial**: GitHub Actions macOS runners are virtualized, and on Apple Silicon
`system_profiler` may redact/omit the serial unless elevated. The official agent
(`MacOS/Bios.pm`, `MacOS/Hardware.pm`) resolves this with a layered chain, which we
mirror exactly and then extend with the project's existing "serial-of-last-resort = UUID"
rule (already used for VirtualBox on Linux via `sysutil.VirtualBoxSerial`).

**Serial (BIOS.SSN):**
1. `system_profiler SPHardwareDataType` → `Serial Number`
2. → `Serial Number (system)` (10.5.7+ wording change)
3. `ioreg -d2 -c IOPlatformExpertDevice` → `IOPlatformSerialNumber`
4. **last resort:** if still empty but a UUID exists → use the UUID as the serial
   (so the GLPI asset is never serial-less / never collides on an empty key).

**UUID (HARDWARE.UUID):**
1. `system_profiler SPHardwareDataType` → `Hardware UUID`
2. `ioreg -d2 -c IOPlatformExpertDevice` → `IOPlatformUUID`

**Manufacturer / Model:**
- Manufacturer: `ioreg` `manufacturer`, else hardcode `Apple Inc.`
- Model: `Model Identifier` → `Machine Model` → `ioreg` `model`

Every identity string runs through `sysutil.CleanDMI` so placeholder/zeroed values are
dropped before the fallback decides whether a field is "empty". A dedicated unit test
covers: full system_profiler data; serial-redacted (UUID-only) → serial falls back to
UUID; both empty → no serial emitted (and the inventory still validates).

## Architecture / build tags

- `internal/collector/macos/*.go` — `//go:build darwin`; pure parsers in `parse.go`
  (no build tag).
- `internal/agent/register_darwin.go` — `//go:build darwin`, blank-imports the package.
- `internal/collector/macos/doc.go` — `//go:build darwin`, declares the package so the
  blank import resolves even before collectors land.
- Reused unchanged: `logger_unix.go` (`!windows`), `generic/{users,hostname,processes,
  timezone}` (timezone_other.go is `!windows && !freebsd` → covers darwin; `/etc/localtime`
  is a symlink to zoneinfo on macOS, which the generic resolver already follows).
- Install prefix: add `internal/config/paths_darwin.go` returning `/usr/local/go-glpi-agent`
  (writable, SIP-safe; retag `paths_unix.go` to `!windows && !darwin`). The `.pkg` payload
  installs there + a `LaunchDaemon` plist under `/Library/LaunchDaemons`.

> Note on `generic/users`: macOS keeps real accounts in OpenDirectory, so `/etc/passwd`
> lists only system users. This is an accepted limitation for v1 (system users are still
> reported); a `dscl . list /Users` enrichment is a possible follow-up.

## Packaging & CI

- **Binaries:** `CGO_ENABLED=0 GOOS=darwin GOARCH=amd64` and `…GOARCH=arm64`. Optionally
  `lipo -create` a universal binary; the `.pkg`/`.dmg` are still emitted per-arch to match
  upstream naming and `hostArchitectures`.
- **`.pkg`:** `pkgbuild --root <payload> --scripts <scripts> --identifier com.glpi.go-agent
  --install-location /` then `productbuild --distribution Distribution.xml … <out>.pkg`.
  Payload = binary + agent.cfg + LaunchDaemon; `postinstall` loads the daemon.
- **`.dmg`:** `hdiutil create -volname … -srcfolder <pkg> <out>.dmg` (matches upstream,
  which ships the `.pkg` inside the `.dmg`).
- **Artifacts:** `go-glpi-agent_<version>_x86_64.pkg`, `…_x86_64.dmg`,
  `…_arm64.pkg`, `…_arm64.dmg`.
- **CI matrix:** `macos-13` (Intel/x86_64) + `macos-latest` (Apple Silicon/arm64). Each:
  build → `run --local` + `GFI_DUMP_JSON` → `check-jsonschema` vs GLPI
  `inventory.schema.json` → **install the official GLPI-Agent from its
  `GLPI-Agent-1.18_<arch>.pkg`** (`sudo installer -pkg … -target /`) → run it →
  print a per-section item-count comparison (Go vs official) → upload `.pkg`/`.dmg`.

## Risks / tradeoffs

- **Unsigned installers** → Gatekeeper warns on download. Acceptable for internal/MDM
  push; signing is a documented follow-up needing certificates.
- **CI VM identity** → handled by the serial→UUID fallback above; asserted in CI.
- **Apple Silicon CPU fields** differ from Intel → parser handles both (Chip vs
  brand_string); covered by unit tests with both sample outputs.
- **`system_profiler` latency** (esp. `SPApplicationsDataType`) → each collector keeps
  the engine's per-collector timeout; software is the slow one and runs concurrently.
