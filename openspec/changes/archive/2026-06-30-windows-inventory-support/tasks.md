## 1. Cross-platform build foundation

- [x] 1.1 Split the logger: move the syslog backend into `internal/logger/logger_unix.go` (`//go:build !windows`); add `internal/logger/logger_windows.go` that makes `logger = Syslog` fall back to stderr/file; keep `New`, levels and Stderr/File backends in shared `logger.go`.
- [x] 1.2 Make defaults OS-aware: add `defaultBaseDir()` resolving to `/opt/go-glpi-agent` (unix) vs `%ProgramData%\go-glpi-agent` (windows); update `config.Default()` (`VarDir`, logfile) and `DefaultConfFile` to derive from it.
- [x] 1.3 Add `//go:build linux` to all `internal/collector/linux/*.go` files.
- [x] 1.4 Replace the unconditional `linux`/`generic` blank imports in `internal/agent/agent.go` with `register_linux.go` (`//go:build linux`) and `register_windows.go` (`//go:build windows`); keep `generic` registered on all platforms.
- [x] 1.5 Add `internal/collector/windows/doc.go` (build-tagged) declaring the package so blank imports resolve; verify `CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build ./...` succeeds and `go build ./... && go test ./...` still pass on Linux.

## 2. Dependencies

- [x] 2.1 Promote `github.com/yusufpapurcu/wmi` from indirect to direct in `go.mod`; add `golang.org/x/sys/windows/registry` usage; run `go mod tidy` and confirm no new top-level modules are downloaded.
- [x] 2.2 Extract the junk-value filter (`cleanDMI` in `linux/bios.go`) into a shared helper (e.g. `internal/sysutil`) reusable by Windows WMI string fields.

## 3. Windows collectors — OS, CPU, memory

- [x] 3.1 `windows/os.go`: OS name/edition/version/build, kernel, arch, boot time via `gopsutil/host` + registry `CurrentVersion` fallback; fill OPERATINGSYSTEM + HARDWARE.
- [x] 3.2 `windows/cpu.go`: one CPU per socket (name, manufacturer, speed, cores, threads, arch) via `gopsutil/cpu`.
- [x] 3.3 `windows/memory.go`: total/swap via `gopsutil/mem`; physical slots via WMI `Win32_PhysicalMemory` (skip empty slots).

## 4. Windows collectors — BIOS, storage, USB

- [x] 4.1 `windows/bios.go`: BIOS/system/baseboard/chassis + UUID via `Win32_BIOS`/`Win32_ComputerSystem`/`Win32_BaseBoard`/`Win32_SystemEnclosure`/`Win32_ComputerSystemProduct`, run all strings through the shared junk filter.
- [x] 4.2 `windows/storage.go`: physical disks via `Win32_DiskDrive` (classify NVMe/SSD/HDD); logical drives/filesystems via `gopsutil/disk`.
- [x] 4.3 `windows/usb.go`: connected USB devices via WMI `Win32_PnPEntity` (vendor/product id, name, serial), skipping hubs.

## 5. Windows collectors — network, software, users

- [x] 5.1 `windows/network.go`: interfaces/addresses via `gopsutil/net` enriched by `Win32_NetworkAdapter[Configuration]` (type, gateway, speed); one entry per IP, address-less fallback.
- [x] 5.2 `windows/software.go`: enumerate HKLM + WOW6432Node + HKCU uninstall keys via `x/sys/windows/registry`; map name/version/publisher/install-date/size; skip `SystemComponent=1`/no `DisplayName`; de-dupe by name+version.
- [x] 5.3 `windows/users.go`: local users/groups via `Win32_UserAccount`/`Win32_Group` (local scope); `LastLoggedUser` from `Win32_ComputerSystem.UserName`.
- [x] 5.4 Confirm `generic/timezone` and `generic/processes` (scan-processes) work on Windows; adjust if any POSIX assumption leaks.

## 6. Serialization & schema validation

- [x] 6.1 Run an inventory on a Windows host with `--local` and verify the XML envelope/sections.
- [x] 6.2 Run `run` with `GFI_DUMP_JSON` set; validate the dump against GLPI's `inventory.schema.json`; fix any date/arch/type normalization gaps in `json.go` if Windows surfaces new values.

## 7. Packaging (Windows distribution)

- [x] 7.1 Add a `build-windows` Makefile target (`CGO_ENABLED=0 GOOS=windows GOARCH=amd64` → `dist/go-glpi-agent.exe`).
- [x] 7.2 Add `contrib/windows/agent.cfg` (Windows defaults/paths) and `contrib/windows/install.ps1` / `uninstall.ps1` registering a `go-glpi-agent` Scheduled Task (hourly, SYSTEM), no-clobber config, state-preserving uninstall.
- [x] 7.3 Extend `release.yml` to build the `.exe`, zip `go-glpi-agent_<version>_windows_amd64.zip` (exe + agent.cfg + scripts), add it to checksums and the GitHub release.
- [x] 7.4 Add a `GOOS=windows go build ./...` compile-check step to `go.yml`.

## 8. Integration tests (Vagrant + GLPI)

- [x] 8.1 Add `test/vagrant-windows/` with a `gusztavvargadr/windows-server-2022-standard` box and a WinRM PowerShell provisioner that copies `dist/go-glpi-agent.exe`, installs it, and runs `run --local` then `run --server <glpi>`.
- [x] 8.2 Point the provisioner at the `test/glpi/` docker GLPI 10 stack; confirm a Windows computer asset appears with CPU/memory/disks/network/software populated (HTTP 200, schema-valid).
- [x] 8.3 Document the Windows test flow in `test/README.md`.

## 9. Unit tests & docs

- [x] 9.1 Table-driven test for the registry-software parser (DisplayName/Version/Publisher/SystemComponent handling, de-dup) using fixture rows — no live registry.
- [x] 9.2 Test for WMI→inventory struct mapping using mocked WMI result structs (incl. junk-value filtering).
- [x] 9.3 Update `README.md` (Windows install/usage, supported collectors, tested Windows versions) and `AGENTS.md` (build tags, per-OS layout, WMI/registry conventions).
- [x] 9.4 Update `CHANGELOG.md`; final `go vet ./...`, `go test ./...`, and `GOOS=windows go build ./...` all green.
