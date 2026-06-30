## Why

The per-OS structure introduced for Windows and reused for FreeBSD (build-tagged
collector packages, `register_<goos>.go`, cross-platform `generic` collectors,
OS-split logger/paths) was designed so adding an OS is "drop in a package + one
registration file". **macOS** is the last mainstream desktop/laptop OS a GLPI fleet
needs to inventory, and the official glpi-agent ships first-class macOS support with
signed `.pkg`/`.dmg` installers for both Intel (`x86_64`) and Apple Silicon (`arm64`).
Most of the foundation already covers macOS for free — `logger_unix.go` (syslog),
`paths_unix.go` and `generic/{users,hostname,processes,timezone}` are all
`//go:build !windows`, and gopsutil supports darwin for cpu/mem/disk/net/host/process.

Unlike the official agent — which bundles a full Perl + OpenSSL + ~20 CPAN modules
runtime and needs a multi-stage source build (`contrib/macosx/glpi-agent-packaging.sh`)
— our agent is a **single static Go binary**. macOS packaging therefore collapses to
"cross-compile two binaries, then `pkgbuild`/`productbuild` + `hdiutil`", which is fast
enough to run end-to-end on GitHub Actions macOS runners with no signing infrastructure.

## What Changes

- Add a `darwin` collector package (`internal/collector/macos/`, build-tagged
  `//go:build darwin`) mirroring the existing plugin pattern, covering: OS/kernel,
  CPU, memory, system/BIOS identity (model/serial/UUID/boot-ROM), physical disks,
  filesystems, network, USB, installed software.
- Register it via `internal/agent/register_darwin.go` (`//go:build darwin`); Linux/
  Windows/FreeBSD registration and the `generic` collectors are untouched.
- Reuse the cross-platform foundation as-is: `gopsutil` for cpu/mem/disk/net/host/
  process; `generic/{users,hostname,processes,timezone}` already build on darwin.
  No new Go dependency.
- Source macOS-native data without root where possible: **`system_profiler -json`**
  (`SPHardwareDataType` for model/serial/UUID/boot-ROM, `SPMemoryDataType` for slots,
  `SPNVMeDataType`/`SPSerialATADataType` for disks, `SPUSBDataType` for USB,
  `SPApplicationsDataType` for software), **`sw_vers`** + **`uname`** for OS/kernel,
  **`sysctl machdep.cpu.*`/`hw.*`** for CPU details, and **`route -n get default`**
  for the gateway. Identity strings flow through `sysutil.CleanDMI`.
- Build for **both architectures**: cross-compiled static `darwin/amd64` (Intel) and
  `darwin/arm64` (Apple Silicon) binaries (`CGO_ENABLED=0`), optionally fused into a
  universal binary with `lipo` on the macOS runner.
- Produce native macOS installers: per-arch `.pkg` (via `pkgbuild` + `productbuild`
  with a `LaunchDaemon` for scheduled runs and pre/postinstall scripts) wrapped in a
  `.dmg` (via `hdiutil`), named `go-glpi-agent_<version>_x86_64.{pkg,dmg}` and
  `go-glpi-agent_<version>_arm64.{pkg,dmg}` to match the official artifact naming.
- Add a `contrib/macos/` directory: `agent.cfg`, a `com.glpi.go-agent.plist`
  LaunchDaemon, `pkgbuild` `preinstall`/`postinstall` scripts, an `uninstall.sh`,
  and the `pkg`/`dmg` build driver script.
- **Validate everything on GitHub Actions** (the only test environment): a macOS CI
  job builds the binary, runs a real inventory, validates the native JSON against
  GLPI's `inventory.schema.json`, then **installs the official GLPI-Agent from its
  `.dmg`/`.pkg` release, runs it, and prints a per-section count comparison** of the
  two agents' output. The job runs on both an Apple-Silicon runner (`macos-latest`)
  and an Intel runner (`macos-13`), and uploads the `.pkg`/`.dmg` as build artifacts.

## Capabilities

### New Capabilities
- `macos-os`: operating system / kernel (Darwin) / product version / build / arch / boot time / hostname / timezone on macOS.
- `macos-hardware`: CPU (Intel + Apple Silicon), memory (+ slots), system identity & boot-ROM (BIOS section) via `system_profiler`, physical disks, filesystems, USB.
- `macos-network`: network interfaces, addresses, MAC, type (ethernet/wifi/loopback), gateway, status.
- `macos-software`: installed applications via `system_profiler SPApplicationsDataType`.
- `macos-packaging`: dual-arch (x86_64 + arm64) binaries, `.pkg` + `.dmg` installers with a `LaunchDaemon`, the GitHub Actions build matrix, and the CI validation/comparison-against-the-official-agent path.

### Modified Capabilities
- `platform-portability`: add a requirement that the module also compiles and registers collectors for `GOOS=darwin` (both `amd64` and `arm64`), extending the existing Linux/Windows/FreeBSD compile + per-OS registration requirements. Pure addition — existing behavior is unchanged.

## Impact

- **Code**: new `internal/collector/macos/`; `internal/agent/register_darwin.go`;
  possibly `internal/config/paths_darwin.go` (macOS install prefix). Logger unchanged
  (already `!windows`); `generic/*` reused as-is.
- **Dependencies**: none new (gopsutil + cobra cover it).
- **Build/CI**: `Makefile` gains `build-macos-amd64`/`build-macos-arm64`/
  `package-macos`; a new `.github/workflows/macos.yml` runs build + inventory +
  schema validation + comparison on `macos-latest` (arm64) and `macos-13` (x86_64);
  `release.yml` publishes the four installers (`x86_64`/`arm64` × `pkg`/`dmg`).
- **Packaging**: `contrib/macos/` (LaunchDaemon, pre/postinstall, uninstall, build driver).
- **Tests**: pure parsers for the `system_profiler -json` and `sysctl` output get
  unit tests that run on Linux CI; full inventory + comparison runs on macOS CI.
- **Out of scope**: Apple Developer code-signing & notarization (artifacts are
  unsigned — documented as a follow-up that needs paid certificates/secrets); a
  Homebrew tap/formula; MDM-profile / antivirus / firewall sub-inventories.
