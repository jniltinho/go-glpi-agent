## Context

go-glpi-agent is a single-binary inventory agent. The transport layer (native GLPI JSON +
legacy XML/PROLOG, in `internal/transport/server`), the inventory data model
(`internal/inventory`), the collector engine/registry (`internal/collector`), the config
parser and the CLI are already platform-neutral in spirit — but the binary only builds and
runs on Linux today. Two things actively block a Windows build:

1. `internal/logger/logger.go` imports `log/syslog`, which has no Windows implementation, so
   `GOOS=windows go build` fails to compile.
2. `config.Default()` and `DefaultConfFile` hardcode `/opt/go-glpi-agent/...`.

Everything else (collectors) is gated at runtime by `IsEnabled` returning false off-Linux,
so it would be dead weight rather than a compile error — but the WMI-based Windows collectors
we are adding *do* need Windows-only imports, which forces a clean build-tag split.

The collector pattern is the key asset: `Collector{Name,Category,IsEnabled,Collect}` with
`init()` self-registration and a concurrent engine. Adding a platform is "write a package of
collectors + register it", not "rework the core". This change keeps that contract.

## Goals / Non-Goals

**Goals:**
- `GOOS=windows GOARCH=amd64 go build ./...` succeeds and the resulting `.exe` inventories a
  Windows host into the same GLPI 10+ server via the existing transport code.
- Feature parity with the Linux collector set: OS, CPU, memory(+slots), BIOS/board/chassis/UUID,
  physical disks, logical drives, USB, network, software, users, timezone, processes.
- Native JSON output passes GLPI's `inventory.schema.json` (same normalizers already in `json.go`).
- A Windows distribution (`.exe` + `agent.cfg` + install/uninstall scripts → Scheduled Task)
  built on the existing Linux CI runner.
- Reproducible Windows test path via Vagrant against the existing GLPI docker stack.

**Non-Goals:**
- macOS/BSD/Solaris support.
- Parity for categories the Linux side also lacks (GPU, monitors/EDID, printers, PCI, batteries).
- A polished MSI installer (WiX needs a Windows runner) — tracked as a follow-up; the `.zip`
  + Scheduled Task is the v1 deliverable.
- Running as a true Windows Service in v1 — the Scheduled Task mirrors the recommended Linux
  systemd *timer* path; SCM/service integration is optional/follow-up.

## Decisions

### D1: Reuse gopsutil; add WMI + registry only where gopsutil can't reach

`shirou/gopsutil` is already a dependency and is genuinely cross-platform (no cgo) — its
`cpu`, `mem`, `disk`, `net`, `host`, `process` subpackages already work on Windows and back
much of the data we need. For data gopsutil does not expose (physical memory slots,
BIOS/baseboard/chassis serials, system UUID, adapter type/gateway, installed software), use
**WMI** via `github.com/yusufpapurcu/wmi`. That library is *already pulled in as an indirect
dependency of gopsutil*, so promoting it to a direct dependency adds zero new modules. For
installed software, use `golang.org/x/sys/windows/registry` (the `x/sys` module is already
indirect) instead of WMI.

- **Alternative — StackExchange/wmi**: upstream now redirects to the `yusufpapurcu/wmi` fork;
  the fork is the maintained one and the one gopsutil uses. Rejected to avoid a second WMI lib.
- **Alternative — `Win32_Product` for software**: rejected. It is notoriously slow and triggers
  an MSI consistency check (self-repair) on every row. The registry uninstall keys are the
  standard, fast, side-effect-free source (this is what glpi-agent itself does).
- **Alternative — shell out to `powershell`/`wmic`**: rejected. Fragile parsing, `wmic` is
  deprecated, slower process spawns. WMI/registry bindings are typed and in-process.

### D2: Build-tag split for collectors and logger

- Add `//go:build windows` to every file in the new `internal/collector/windows/` package.
- Add `//go:build linux` to the existing `internal/collector/linux/*.go` files so they don't
  compile into the Windows binary (and to make the platform boundary explicit).
- Split the logger: `logger_unix.go` (`//go:build !windows`) keeps the syslog backend;
  `logger_windows.go` provides a syslog→fallback shim (stderr/file; optionally Windows Event
  Log later). `New`, levels and the `Stderr`/`File` backends stay shared in `logger.go`.
- Registration: `internal/agent/agent.go` currently blank-imports `linux` + `generic`
  unconditionally. Replace those two blank imports with per-OS files —
  `register_linux.go` (`//go:build linux`, imports `linux`) and `register_windows.go`
  (`//go:build windows`, imports `windows`) — keeping the `generic` import shared. This
  satisfies the `platform-portability` requirement "only this OS's collectors are registered".

- **Alternative — keep one package, gate with `runtime.GOOS`**: impossible, because the WMI
  imports won't compile on Linux. Build tags are mandatory once we touch WMI.

### D3: OS-aware defaults

Make `config.Default()` and `DefaultConfFile` resolve per-OS. Cleanest implementation: two
small build-tagged files in `internal/config` — `paths_unix.go` / `paths_windows.go` —
exposing `defaultBaseDir()` (`/opt/go-glpi-agent` vs `os.Getenv("ProgramData")\go-glpi-agent`),
which `Default()` and the conf-file constant consume. The Windows `agent.cfg`, `vardir`
(`...\var`) and logfile defaults derive from that base.

- **Alternative — `runtime.GOOS` switch in one file**: works (no Windows-only imports needed
  for paths), and is the laziest. Acceptable too; the build-tag split is only *preferred* for
  symmetry with the logger. Either is fine — pick one in implementation, don't do both.

### D4: Windows distribution = zip + PowerShell + Scheduled Task

Cross-compile on the existing ubuntu release runner (`CGO_ENABLED=0 GOOS=windows`), then zip
the `.exe`, a Windows `agent.cfg`, and `install.ps1`/`uninstall.ps1`. `install.ps1` copies to
`%ProgramFiles%\go-glpi-agent`, seeds `%ProgramData%\go-glpi-agent\agent.cfg` (no-clobber),
and registers a Scheduled Task running `run` hourly as SYSTEM — the direct analog of the
`go-glpi-agent.timer` unit. nfpm does not target Windows, so we don't use it here.

- **Alternative — WiX MSI**: nicer UX, but WiX only runs on Windows, forcing a `windows-latest`
  CI job. Deferred to a follow-up; out of scope for v1.
- **Alternative — NSIS cross-built from Linux**: technically possible (gpg4win does this) but
  adds toolchain setup for marginal benefit over a documented zip + script. Deferred.
- **Alternative — Windows Service via `kardianos/service` or `x/sys/windows/svc`**: a real
  option for `daemon` mode, but adds service lifecycle code/deps. The Scheduled Task covers the
  recommended "periodic oneshot" deployment with zero Go code, mirroring the Linux timer.

### D5: Mapping WMI/registry to the existing inventory model

No new inventory fields are required — the `internal/inventory` model and the XML/JSON
serializers already cover every Windows datum:
- `Win32_BIOS`/`Win32_ComputerSystem`/`Win32_BaseBoard`/`Win32_SystemEnclosure`/
  `Win32_ComputerSystemProduct` → `inventory.BIOS` + `Hardware.UUID`/`ChassisType`.
- `Win32_PhysicalMemory` → `inventory.Memory` slots; `gopsutil/mem` → `Hardware.Memory/Swap`.
- `Win32_DiskDrive` → `inventory.Storage`; `gopsutil/disk` → `inventory.Drive`.
- `Win32_NetworkAdapter[Configuration]` + `gopsutil/net` → `inventory.Network`.
- `Win32_PnPEntity` (USB) → `inventory.USBDevice`.
- Registry uninstall keys → `inventory.Software`.
- `Win32_UserAccount`/`Win32_Group`/`Win32_ComputerSystem.UserName` → `LocalUser`/`LocalGroup`/`LastLoggedUser`.
The existing `cleanDMI`-style junk filtering is reused (extract it to a shared helper so both
Linux DMI and Windows WMI strings go through it). `archGLPI` already maps `amd64→x86_64`.

## Risks / Trade-offs

- **WMI is slow / can hang under load** → keep the existing per-collector timeout in the
  engine; query only the specific WMI classes/columns needed; never use `Win32_Product`.
- **WMI/registry need elevation for some fields** (full software list, some serials) → degrade
  gracefully (same pattern as Linux dmidecode): missing data is empty, not a failed cycle. The
  Scheduled Task runs as SYSTEM so the service path gets full data.
- **Windows VMs are heavy/licensing-bound in CI** → Vagrant Windows tests run on a test host,
  not GitHub Actions (same stance as the Linux Vagrant matrix); CI only does the
  `GOOS=windows go build` compile check.
- **`gopsutil/host` may under-report Windows edition/build** → registry fallback for
  `CurrentVersion` (build/UBR/edition).
- **Signal handling under Windows** → `daemon` uses `signal.NotifyContext(SIGTERM,SIGINT)`;
  `SIGTERM` is never delivered on Windows but `SIGINT` (Ctrl+C) is, and the primary deployment
  is the Scheduled Task (`run`, no long-lived process), so this is acceptable for v1.
- **Indirect→direct dep churn** → only `go.mod`'s `require` block changes; no new modules
  downloaded since `yusufpapurcu/wmi` and `x/sys` are already in the build graph.

## Migration Plan

1. Land the portability refactor (logger split, OS-aware defaults, per-OS registration) — this
   is behavior-preserving on Linux and independently testable (`go test ./...` + `GOOS=windows
   go build`).
2. Add the `windows` collector package incrementally (OS/CPU/mem first, then BIOS/storage/net,
   then software/users), each verifiable in the Vagrant Windows VM against `--local` XML.
3. Add packaging (Makefile target, release zip, install scripts) and the Vagrant Windows infra.
4. Validate native JSON against `inventory.schema.json`, then ship.

Rollback is trivial: Windows artifacts are additive; the Linux build/release path is unchanged,
so reverting the change leaves Linux releases working.

## Open Questions

- Event Log vs file as the Windows `Syslog`-fallback backend? (v1 defaults to stderr/file; Event
  Log is a small follow-up.)
- Do we want a `service install/uninstall` subcommand in v1, or is the Scheduled Task script
  sufficient? (Leaning: script only for v1.)
- Which Windows base box for CI/test reproducibility — `windows-server-2022-standard` only, or
  also a `windows-10`/`windows-11` desktop box for the desktop software/user paths?
