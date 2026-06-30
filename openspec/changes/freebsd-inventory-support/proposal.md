## Why

The per-OS structure introduced for Windows (build-tagged collector packages,
`register_<goos>.go`, cross-platform `generic` collectors, OS-split logger/paths)
was explicitly designed so adding an OS is "drop in a package + one registration
file". FreeBSD is the natural next target: it runs on a lot of infrastructure
(firewalls, storage/NAS, hypervisors) that GLPI fleets need to inventory, and the
official glpi-agent supports it. Much of the foundation already covers FreeBSD for
free — `logger_unix.go` (syslog), `paths_unix.go`, and `generic/users` are all
`//go:build !windows`, and gopsutil supports FreeBSD for cpu/mem/disk/net/host/process.

## What Changes

- Add a `freebsd` collector package (`internal/collector/freebsd/`, build-tagged
  `//go:build freebsd`) mirroring the existing plugin pattern, covering: OS/kernel,
  CPU, memory, BIOS/board/chassis/UUID, physical disks, filesystems, network, USB,
  installed software.
- Register it via `internal/agent/register_freebsd.go` (`//go:build freebsd`); the
  Linux/Windows registration and `generic` collectors are untouched.
- Reuse the cross-platform foundation as-is: `gopsutil` for cpu/mem/disk/net/host/
  process; `generic/{users,hostname,processes}` already work on FreeBSD. No new
  Go dependency.
- Source FreeBSD-native data without root where possible: **`kenv smbios.*`** for
  BIOS/board/chassis/serial/UUID (the FreeBSD analog of `/sys/class/dmi`), **`pkg
  query`** for software, **`geom`/`camcontrol`** for physical disks, **sysctl** for
  CPU/arch details, and **`usbconfig`** for USB devices.
- Small `generic` tweak: timezone resolution gets a FreeBSD path (`/var/db/zoneinfo`),
  since FreeBSD has no `/etc/timezone` and `/etc/localtime` is a copy, not a symlink.
- Produce a FreeBSD distribution: a cross-compiled static `freebsd/amd64` binary,
  packaged as a tarball with an `rc.d` service script and a `periodic`/cron entry for
  scheduled runs (the FreeBSD analog of the systemd timer). A native `.pkg` is a follow-up.
- Add FreeBSD integration testing: a `freebsd/FreeBSD-14` Vagrant box that installs the
  agent and sends an inventory to the GLPI 10 docker stack in `test/glpi/`, comparing
  against the official glpi-agent when available.

## Capabilities

### New Capabilities
- `freebsd-os`: operating system / kernel / version / arch / boot time / hostname / timezone on FreeBSD.
- `freebsd-hardware`: CPU, memory, BIOS/board/chassis/UUID (via kenv smbios), physical disks (geom/camcontrol), filesystems, USB.
- `freebsd-network`: network interfaces, addresses, MAC, type, gateway, status.
- `freebsd-software`: installed packages via `pkg query`.
- `freebsd-packaging`: FreeBSD binary, tarball distribution, rc.d service + periodic/cron, and the Vagrant + GLPI integration test path.

### Modified Capabilities
- `platform-portability`: add a requirement that the module also compiles and registers collectors for `GOOS=freebsd` (extending the existing Linux/Windows compile + per-OS registration requirements). Pure addition — existing Linux/Windows behavior is unchanged.

## Impact

- **Code**: new `internal/collector/freebsd/`; `internal/agent/register_freebsd.go`;
  `internal/collector/generic/timezone_freebsd.go` (+ retag `timezone_other.go` to
  `!windows && !freebsd`). Logger/config/paths unchanged (already `!windows`).
- **Dependencies**: none new (gopsutil + cobra cover it).
- **Build/CI**: `Makefile` gains `build-freebsd`/`package-freebsd`; `go.yml` adds a
  `GOOS=freebsd go build`/`vet` compile check.
- **Packaging**: `contrib/freebsd/` (rc.d script, agent.cfg, periodic/cron, install notes).
- **Tests**: `test/vagrant-freebsd/`; unit tests for the `pkg query` and `kenv smbios` parsers.
- **Out of scope**: a native FreeBSD `.pkg`/ports Makefile (follow-up); arm64; macOS/other BSDs.
