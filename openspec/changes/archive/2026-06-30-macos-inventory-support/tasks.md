## 1. Foundation (cross-platform tweaks)

- [x] 1.1 Add `internal/agent/register_darwin.go` (`//go:build darwin`) blank-importing the macos collector package.
- [x] 1.2 Add `internal/collector/macos/doc.go` (`//go:build darwin`) declaring the package so the blank import resolves.
- [x] 1.3 Install prefix: add `internal/config/paths_darwin.go` (`defaultBaseDir` → `/usr/local/go-glpi-agent`); retag `paths_unix.go` to `//go:build !windows && !darwin`.
- [x] 1.4 Confirm `generic/{users,hostname,processes,timezone}` build on darwin (timezone_other.go is `!windows && !freebsd`, covers darwin); fix any leaked Linux assumption.
- [x] 1.5 Verify `CGO_ENABLED=0 GOOS=darwin GOARCH=amd64 go build ./...` and `…GOARCH=arm64 go build ./...` succeed and Linux/Windows/FreeBSD builds still pass.

## 2. macOS collectors — OS, CPU, memory

- [x] 2.1 `macos/os.go`: OS name/product version/build/kernel(Darwin)/arch/boot time via `sw_vers` + `uname` + gopsutil/host; fill OPERATINGSYSTEM + HARDWARE (NAME from `system_profiler SPSoftwareDataType` "System Version").
- [x] 2.2 `macos/cpu.go`: CPU model/cores/threads/speed/arch via `sysctl machdep.cpu.*`/`hw.*` (Intel) with `system_profiler SPHardwareDataType` "Chip" + `hw.model` fallback for Apple Silicon; via gopsutil/cpu for counts.
- [x] 2.3 `macos/memory.go`: total/swap via gopsutil/mem; per-slot entries via `system_profiler SPMemoryDataType` (single unified entry on Apple Silicon).

## 3. macOS collectors — system identity (BIOS), storage, USB

- [x] 3.1 `macos/bios.go`: SMANUFACTURER/SMODEL/SSN/BVERSION via `system_profiler SPHardwareDataType`, with `ioreg -d2 -c IOPlatformExpertDevice` fallback; **serial fallback chain** `Serial Number` → `Serial Number (system)` → `IOPlatformSerialNumber`; all strings via `sysutil.CleanDMI`.
- [x] 3.2 **UUID + serial-of-last-resort**: UUID via `Hardware UUID` → `IOPlatformUUID`; if SSN is still empty but a UUID exists, set SSN = UUID (extend/reuse the `sysutil.VirtualBoxSerial` "serial = UUID" pattern) so the host is never serial-less — critical on virtualized CI runners. Set HARDWARE.UUID too.
- [x] 3.3 `macos/storage.go`: physical disks via `system_profiler SPNVMeDataType` + `SPSerialATADataType` (model/serial/size/type); filesystems via gopsutil/disk, skipping pseudo mounts (devfs/autofs/...).
- [x] 3.4 `macos/usb.go`: USB devices via `system_profiler SPUSBDataType` (vendor/product id, manufacturer, name, serial), skipping hubs.

## 4. macOS collectors — network, software

- [x] 4.1 `macos/network.go`: interfaces/addresses via gopsutil/net; default gateway via `route -n get default`; one entry per IP with address-less fallback; type (loopback/wifi/ethernet) from name + flags.
- [x] 4.2 `macos/software.go`: installed apps via `system_profiler SPApplicationsDataType -json` → name/version/publisher/install-date, `FROM=system_profiler`; degrade to empty if unavailable.

## 5. Pure parsers + unit tests (run on Linux CI)

- [x] 5.1 Extract the `system_profiler -json` decoders and the `sysctl`/`sw_vers` line parsers into a build-tag-free `parse.go` in the macos package (no darwin-only calls).
- [x] 5.2 `parse_test.go` (no build tag): table-driven tests for SPHardware/SPMemory/SPNVMe/SPUSB/SPApplications parsing and the sysctl CPU mapping (Intel **and** Apple Silicon samples).
- [x] 5.3 Dedicated **serial/UUID fallback** test: (a) full data; (b) serial redacted, UUID present → SSN falls back to UUID; (c) both empty → no serial emitted; (d) junk/zeroed serial via `CleanDMI` → treated as empty then falls back.

## 6. Serialization & schema validation

- [x] 6.1 Run an inventory on the macOS CI runner with `--local` and verify the XML envelope/sections.
- [x] 6.2 Run `run` with `GFI_DUMP_JSON`; validate the dump against GLPI's `inventory.schema.json`; fix any date/arch/type normalization gaps macOS surfaces (e.g. arch `arm64`).

## 7. Packaging (dual-arch .pkg + .dmg)

- [x] 7.1 Add Makefile targets `build-macos-amd64`, `build-macos-arm64` (`CGO_ENABLED=0 GOOS=darwin GOARCH=<arch>`), and an optional `lipo` universal target.
- [x] 7.2 Add `contrib/macos/`: `agent.cfg`, `com.glpi.go-agent.plist` (LaunchDaemon, periodic run), `preinstall`/`postinstall` (load daemon), `uninstall.sh`, `Distribution.xml`.
- [x] 7.3 Add `package-macos` (and per-arch driver script `contrib/macos/build-pkg.sh`): `pkgbuild` + `productbuild` → `go-glpi-agent_<version>_<arch>.pkg`, then `hdiutil create` → `…_<arch>.dmg`. Runs on the macOS runner.
- [x] 7.4 Extend `release.yml` (or a macOS release job) to build + publish all four installers (`x86_64`/`arm64` × `pkg`/`dmg`) and add them to `checksums.txt` + the GitHub release.

## 8. CI validation on GitHub Actions (the only test environment)

- [x] 8.1 Add `.github/workflows/macos.yml` with a matrix: `macos-13` (Intel/x86_64) + `macos-latest` (Apple Silicon/arm64). Steps: setup-go, `go test ./...`, build the binary.
- [x] 8.2 Run a real inventory (`run --local out --debug` + `GFI_DUMP_JSON`); clone `glpi-project/inventory_format`; `check-jsonschema` the native JSON against `inventory.schema.json`.
- [x] 8.3 **Install the official GLPI-Agent from its release** matching the runner arch (`GLPI-Agent-1.18_x86_64.pkg` / `GLPI-Agent-1.18_arm64.pkg`) via `sudo installer -pkg … -target /`; run it to produce a reference inventory.
- [x] 8.4 Print a **per-section item-count comparison** (Go agent vs official agent) and assert the core hardware sections (BIOS serial/UUID, CPU, memory, OS) are populated by both. Fail if the Go agent's serial **and** UUID are both empty.
- [x] 8.5 Build the `.pkg`/`.dmg` for the runner's arch and `actions/upload-artifact` them; add `GOOS=darwin go build/vet` compile checks to `go.yml`.

## 9. Docs & release

- [x] 9.1 Update `README.md` (macOS install via `.pkg`/`.dmg`; add a macOS column to the per-OS collector table; supported arches/versions) and `AGENTS.md` (macos package, system_profiler/ioreg/sysctl conventions, serial→UUID rule, per-OS layout note).
- [x] 9.2 Update `CHANGELOG.md`; final `go vet ./...`, `go test ./...`, `GOOS=darwin GOARCH=amd64/arm64 go build ./...` (and linux/windows/freebsd) all green; macOS CI workflow green on both runners.
