## Why

go-glpi-agent today is Linux-only: every collector is gated on `runtime.GOOS == "linux"`,
the logger imports `log/syslog` (which does not compile on Windows), and the default
paths (`/opt/go-glpi-agent/...`) are POSIX. GLPI fleets are overwhelmingly mixed, and
Windows is the single biggest gap versus the Perl/official `glpi-agent`. Adding Windows
support lets one binary inventory the whole fleet into the same GLPI 10+ server using the
protocol code that already exists.

## What Changes

- Make the codebase cross-platform compilable: split the syslog backend out of the
  logger so `GOOS=windows go build` succeeds, and make default `vardir`/`conf-file`
  paths OS-aware (`%ProgramData%\go-glpi-agent` on Windows). **BREAKING** only in that
  `config.Default()` becomes platform-dependent.
- Add a `windows` collector package (`internal/collector/windows/`, build-tagged
  `//go:build windows`) mirroring the existing plugin pattern (`Name/Category/IsEnabled/Collect`,
  self-registering via `init()`), covering: OS/edition, CPU, memory (+ physical slots),
  BIOS/baseboard/chassis/UUID, physical disks, logical drives, network, USB, installed
  software, users/groups/logged-in user, timezone, processes.
- Gate the existing Linux collectors behind `//go:build linux` and register collector
  packages per-OS so each platform's binary only carries its own collectors.
- Reuse the dependency already present: `shirou/gopsutil` for the cross-platform signals
  (CPU/mem/disk/net/host/process) and promote `yusufpapurcu/wmi` (currently an indirect
  dep of gopsutil) to a direct dependency for the WMI-only data; add
  `golang.org/x/sys/windows/registry` (from the already-indirect `x/sys`) for installed
  software and OS edition. No heavyweight new dependency.
- Produce a Windows distribution: cross-compiled `go-glpi-agent.exe`, a `.zip` artifact
  with `agent.cfg` + PowerShell `install.ps1`/`uninstall.ps1` that register a **Scheduled
  Task** (the Windows analog of the systemd timer), built entirely on the existing Linux
  release runner. An optional WiX `.msi` (Windows CI job) is a follow-up.
- Add Windows integration test infrastructure: a `gusztavvargadr/windows-server-*` Vagrant
  box (WinRM-provisioned) that installs the agent and sends a real inventory to the GLPI 10
  docker stack already in `test/glpi/`.

## Capabilities

### New Capabilities
- `platform-portability`: cross-platform build (OS-split logger, OS-aware defaults,
  per-OS collector registration via build tags) so the same module builds for linux and windows.
- `windows-os`: operating system, edition/build, kernel, hostname/domain, boot time, timezone.
- `windows-hardware`: CPU, memory + physical slots, BIOS/baseboard/chassis, system UUID,
  physical disks, logical drives, USB devices.
- `windows-network`: network interfaces, addresses, MAC, type, gateway, status.
- `windows-software`: installed software via the registry uninstall keys (HKLM/HKCU +
  WOW6432Node), plus local users/groups and logged-in user.
- `windows-packaging`: Windows binary, zip distribution, install/uninstall scripts,
  Scheduled-Task registration, and the Vagrant + GLPI integration test path.

### Modified Capabilities
<!-- openspec/specs/ is empty (prior change not yet archived); no live capabilities to modify. -->

## Impact

- **Code**: `internal/logger` (split into `logger_unix.go` / `logger_windows.go`);
  `internal/config` (OS-aware `Default()` and `DefaultConfFile`); `internal/agent`
  (per-OS blank-import registration files; verify signal handling under the Windows SCM);
  new `internal/collector/windows/`; build tags on `internal/collector/linux/`.
- **Dependencies**: `yusufpapurcu/wmi` indirect→direct; add `golang.org/x/sys/windows/registry`.
  gopsutil and cobra unchanged.
- **Build/CI**: new `GOOS=windows` build target in the `Makefile`; `release.yml` produces
  the `.zip`; `go.yml` adds a `GOOS=windows go build` compile check.
- **Packaging**: `contrib/windows/` (agent.cfg for Windows, install/uninstall scripts).
- **Tests**: `test/vagrant-windows/`; unit tests for registry-software and WMI struct mapping.
- **Out of scope**: macOS/BSD; GPU/monitors/printers parity; MSI installer (follow-up).
