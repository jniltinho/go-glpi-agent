## 1. Foundation (cross-platform tweaks)

- [x] 1.1 Add `internal/agent/register_freebsd.go` (`//go:build freebsd`) blank-importing the freebsd collector package.
- [x] 1.2 Add `internal/collector/freebsd/doc.go` (`//go:build freebsd`) declaring the package so the blank import resolves.
- [x] 1.3 Timezone: retag `internal/collector/generic/timezone_other.go` to `//go:build !windows && !freebsd`; add `timezone_freebsd.go` whose `osTimezoneName()` reads `/var/db/zoneinfo`.
- [x] 1.4 Verify `CGO_ENABLED=0 GOOS=freebsd GOARCH=amd64 go build ./...` succeeds and `go build/test ./...` (Linux) + `GOOS=windows go build ./...` still pass.

## 2. FreeBSD collectors — OS, CPU, memory

- [x] 2.1 `freebsd/os.go`: OS name/version/kernel/arch/boot time via `gopsutil/host` + sysctl (`kern.ostype`, `kern.osrelease`, `hw.machine_arch`); fill OPERATINGSYSTEM + HARDWARE.
- [x] 2.2 `freebsd/cpu.go`: CPU model/cores/threads/arch via `gopsutil/cpu` + `hw.model`/`hw.ncpu`.
- [x] 2.3 `freebsd/memory.go`: total/swap via `gopsutil/mem`.

## 3. FreeBSD collectors — BIOS, storage, USB

- [x] 3.1 `freebsd/bios.go`: system/bios/baseboard/chassis + UUID via `kenv smbios.*`, all strings through `sysutil.CleanDMI`; emit nothing when no real identity exists.
- [x] 3.2 `freebsd/storage.go`: physical disks via `geom disk list` (model/serial/size; `camcontrol` fallback for serial); filesystems (UFS/ZFS) via `gopsutil/disk`, skipping pseudo mounts (devfs/procfs/fdescfs/...).
- [x] 3.3 `freebsd/usb.go`: USB devices via `usbconfig` (vendor/product id, description), skipping hubs.

## 4. FreeBSD collectors — network, software

- [x] 4.1 `freebsd/network.go`: interfaces/addresses via `gopsutil/net`; default gateway via `route -n get default` (or `netstat -rn`); one entry per IP, address-less fallback; type from name/flags.
- [x] 4.2 `freebsd/software.go`: `pkg query "%n\t%v\t%q\t%sb\t%c"` → name/version/arch/size/comment, `FROM=pkg`; degrade to empty when `pkg` is absent.
- [x] 4.3 Confirm `generic/{users,hostname,processes,timezone}` work on FreeBSD; fix any leaked POSIX/Linux assumption.

## 5. Pure parsers + unit tests

- [x] 5.1 Extract the `pkg query` line parser and the `kenv`/`geom` block parsers into a build-tag-free `parse.go` in the freebsd package (no FreeBSD-only calls), mirroring the windows package.
- [x] 5.2 `parse_test.go` (no build tag): table-driven tests for the pkg-query parser, the kenv smbios mapping (incl. junk filtering), and `geom disk list` parsing — runs on Linux CI.

## 6. Serialization & schema validation

- [x] 6.1 Run an inventory on FreeBSD with `--local` and verify the XML envelope/sections.
- [x] 6.2 Run `run` with `GFI_DUMP_JSON`; validate the dump against GLPI's `inventory.schema.json`; fix any date/arch/type normalization gaps if FreeBSD surfaces new values.

## 7. Packaging (FreeBSD distribution)

- [x] 7.1 Add `build-freebsd` Makefile target (`CGO_ENABLED=0 GOOS=freebsd GOARCH=amd64` → `dist/go-glpi-agent-freebsd`).
- [x] 7.2 Add `contrib/freebsd/` : `agent.cfg`, an `rc.d/go_glpi_agent` service script, and a `periodic`/cron snippet; add a `package-freebsd` target producing `go-glpi-agent_<version>_freebsd_amd64.tar.gz`.
- [x] 7.3 Extend `release.yml` to build + publish the FreeBSD tarball (add to checksums + release).
- [x] 7.4 Add a `GOOS=freebsd go build`/`vet` compile-check step to `go.yml`.

## 8. Integration tests (Vagrant + GLPI + official agent)

- [x] 8.1 Add `test/vagrant-freebsd/` with an official `freebsd/FreeBSD-14` box (POSIX `sh` provisioner) that copies the freebsd binary, runs `run --local` then `run --server <glpi>`.
- [x] 8.2 Provisioner installs the official reference agent (`pkg install fusioninventory-agent`), runs it, and prints a per-section count comparison (Go vs FusionInventory).
- [x] 8.3 Point at the `test/glpi/` GLPI 10 stack; confirm a FreeBSD computer asset appears (HTTP 200, schema-valid). Document the flow in `test/README.md`.

## 9. Docs & release

- [x] 9.1 Update `README.md` (FreeBSD install/usage; add a FreeBSD column to the per-OS collector table; tested versions) and `AGENTS.md` (freebsd package, kenv/pkg/geom conventions, per-OS layout note).
- [x] 9.2 Update `CHANGELOG.md`; final `go vet ./...`, `go test ./...`, `GOOS=freebsd go build ./...` (and linux/windows) all green.
