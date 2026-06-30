## Context

The Windows change established the per-OS pattern: build-tagged collector packages
(`internal/collector/<goos>/`), per-OS registration (`internal/agent/register_<goos>.go`),
cross-platform `generic` collectors, an OS-split logger and OS-aware default paths. FreeBSD
slots straight into this. Crucially, the cross-platform foundation already covers FreeBSD:
`internal/logger/logger_unix.go` (syslog), `internal/config/paths_unix.go` (`/opt`), and
`internal/collector/generic/users.go` are all `//go:build !windows`, so they compile and run
on FreeBSD unchanged. `gopsutil` ships FreeBSD implementations for cpu/mem/disk/net/host/process.

What's left is a `freebsd` collector package for the data gopsutil doesn't expose, one small
`generic` tweak (timezone), and packaging/tests.

## Goals / Non-Goals

**Goals:**
- `GOOS=freebsd GOARCH=amd64 go build ./...` succeeds; the binary inventories a FreeBSD host
  into GLPI 10+ via the existing transport.
- Parity with the Linux/Windows collector set where it makes sense: OS, CPU, memory, BIOS/
  board/chassis/UUID, physical disks, filesystems (UFS/ZFS), network, USB, software.
- Native JSON passes GLPI's `inventory.schema.json`.
- A FreeBSD tarball (binary + rc.d + periodic/cron) built on the Linux CI runner.
- Reproducible validation in a FreeBSD Vagrant VM, compared against the official
  FusionInventory agent.

**Non-Goals:**
- A native FreeBSD `.pkg` / ports Makefile (follow-up; the tarball + rc.d is v1).
- arm64; macOS/NetBSD/OpenBSD.
- Per-DIMM memory slots (FreeBSD has no kenv equivalent; optional dmidecode is a follow-up).

## Decisions

### D1: kenv smbios for BIOS/board/chassis/UUID (no root, no dmidecode)

FreeBSD exposes the SMBIOS table through kernel environment variables: `kenv smbios.system.maker`,
`smbios.system.product`, `smbios.system.serial`, `smbios.system.uuid`, `smbios.bios.vendor`,
`smbios.bios.version`, `smbios.bios.reldate`, `smbios.planar.{maker,product,serial}` (baseboard),
`smbios.chassis.*`. This is the FreeBSD analog of Linux's `/sys/class/dmi/id/` — readable without
root, no external tool. All strings go through the shared `sysutil.CleanDMI` junk filter.

- **Alternative — dmidecode**: needs `pkg install dmidecode` and root. Rejected as the primary
  source; can be a follow-up for per-slot memory (kenv doesn't expose DIMMs).

### D2: pkg query for software

`pkg query "%n\t%v\t%q\t%sb\t%c"` yields name, version, ABI/arch, flat size (bytes), comment —
one line per package, fast and stable. No `Win32_Product`-style pitfalls. Degrades to empty when
`pkg` isn't bootstrapped.

### D3: geom/camcontrol for disks, gopsutil for filesystems

Physical disks: `geom disk list` gives `Mediasize`, `descr` (model) and `ident` (serial) per disk
in a parseable block format; `camcontrol identify` is the fallback for serials. Filesystems
(UFS/ZFS): `gopsutil/disk.Partitions` already enumerates FreeBSD mounts with usage. ZFS datasets
appear as mounts; we skip pseudo filesystems the same way the Linux drive collector does.

### D4: Reuse generic collectors; one timezone tweak

`generic/{users,hostname,processes}` already work on FreeBSD (`/etc/passwd`, `/etc/group`, `who`,
`last`, `os.Hostname`, gopsutil/process). The only gap is timezone: `generic/timezone.go` reads
`/etc/timezone` (Debian-only) and the `/etc/localtime` symlink, but FreeBSD stores the zone name
in `/var/db/zoneinfo` and `/etc/localtime` is a plain copy. Fix: retag `timezone_other.go` to
`//go:build !windows && !freebsd` and add `timezone_freebsd.go` whose `osTimezoneName()` reads
`/var/db/zoneinfo`. The UTC offset already comes from `time.Now().Zone()` (cross-platform).

### D5: Network gateway without /proc

Linux reads `/proc/net/route`; FreeBSD has no `/proc` by default. Use `route -n get default`
(or parse `netstat -rn`) for the default gateway; addresses/MAC/flags come from `gopsutil/net`.
Interface type is derived from name/flags as on Linux (loopback/ethernet/virtual).

### D6: Packaging = tarball + rc.d + periodic, built on Linux

Cross-compile `GOOS=freebsd GOARCH=amd64` on the ubuntu runner, then tar the binary + `agent.cfg`
+ an `rc.d/go_glpi_agent` script + a `periodic`/cron snippet. nfpm doesn't target FreeBSD pkg, and
`pkg create` needs a FreeBSD host — so a native `.pkg`/port is deferred. The tarball is the v1
deliverable (mirrors the Linux tarball + the Windows zip approach).

### D7: Validation against the official FusionInventory agent

The FreeBSD reference agent installs from packages: `pkg install fusioninventory-agent` (the
`sysutils/fusioninventory-agent` port). The `test/vagrant-freebsd` provisioner runs our agent and
the official one, both `--local` for a per-section count comparison and `--server` to the GLPI 10
docker stack, exactly like the Windows glpi-agent comparison. (glpi-agent is also packaged for
FreeBSD and can be swapped in.)

## Risks / Trade-offs

- **kenv keys absent on some VMs/firmware** → degrade gracefully (empty fields, not a failed
  cycle), same pattern as Linux dmidecode / Windows WMI.
- **ZFS vs UFS reporting differences** → rely on gopsutil/disk; verify in the VM that ZFS root
  mounts are reported and pseudo mounts (devfs, procfs, fdescfs) are skipped.
- **FreeBSD Vagrant boxes use rsync + sh, not vboxsf/bash** → the provisioner is POSIX `sh`, and
  files are copied via the file/rsync provisioner (no bashisms; no synced-folder assumptions).
- **`geom`/`camcontrol` output format drift across releases** → keep parsing tolerant (block/kv
  scan), leave a unit test on captured sample output.
- **No new dependency** → only `go.mod` build-graph stays as-is; gopsutil already covers FreeBSD.

## Migration Plan

1. Land the foundation tweak (timezone FreeBSD path; `register_freebsd.go`) + an empty/doc'd
   `freebsd` package so `GOOS=freebsd go build` passes — behavior-preserving elsewhere.
2. Add the freebsd collectors incrementally (os/cpu/mem first, then bios/storage/net/usb, then
   software), each verifiable in the FreeBSD VM via `--local` XML.
3. Add packaging (Makefile target, release tarball, rc.d/periodic) and the Vagrant infra.
4. Validate native JSON against `inventory.schema.json` and compare with FusionInventory; ship.

Rollback is trivial: FreeBSD artifacts are additive; Linux/Windows build/release paths are
unchanged.

## Open Questions

- Tarball install prefix on FreeBSD: `/opt/go-glpi-agent` (consistent with Linux) vs
  `/usr/local` (FreeBSD convention)? Leaning `/opt` for v1 consistency; the `rc.d` script points
  at it.
- Reference agent: FusionInventory (`sysutils/fusioninventory-agent`) or the newer glpi-agent
  package? Default to FusionInventory per the request; both produce the same XML sections.
