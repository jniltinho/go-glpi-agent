# go-glpi-agent — Reference

Detailed documentation for go-glpi-agent. For a quick overview and the 3-step quick
start, see the [README](../README.md).

The agent collects local hardware and software inventory and sends it to a **GLPI 10+**
server using the native JSON protocol, with automatic fallback to the legacy
OCS/FusionInventory XML protocol. It can also write the inventory to a local XML file.
The name is intentionally distinct from the Perl `fusioninventory-agent` and the official
`glpi-agent`, so the three can coexist on the same host.

Supported targets: Linux/amd64, Windows/amd64, FreeBSD/amd64.

## Table of contents

- [Install](#install)
- [Configure](#configure)
- [Run as a service](#run-as-a-service)
- [CLI usage](#cli-usage)
- [Windows](#windows)
- [FreeBSD](#freebsd)
- [Collectors](#collectors)
- [Gaps vs the Perl agent](#gaps-vs-the-perl-agent)
- [Tested distributions](#tested-distributions)
- [Build from source](#build-from-source)
- [Architecture](#architecture)
- [Notes](#notes)

## Install

Download a release artifact from
[Releases](https://github.com/jniltinho/go-glpi-agent/releases), or build from source
(see [Build from source](#build-from-source)). On Linux everything installs under
`/opt/go-glpi-agent`.

```sh
# Debian / Ubuntu
sudo dpkg -i go-glpi-agent_*_amd64.deb

# RHEL / Rocky / Alma / Fedora / openSUSE
sudo rpm -i go-glpi-agent-*.x86_64.rpm

# Arch Linux
sudo pacman -U go-glpi-agent-*-x86_64.pkg.tar.zst

# or the portable tarball
tar -xzf go-glpi-agent_*_linux_amd64.tar.gz -C /opt/go-glpi-agent
```

On **Windows**, download `go-glpi-agent_*_windows_amd64.zip`, extract it, and run
`install.ps1` from an elevated PowerShell — see [Windows](#windows).
On **FreeBSD**, extract `go-glpi-agent_*_freebsd_amd64.tar.gz` and follow `INSTALL.md`
— see [FreeBSD](#freebsd).

The Linux packages install:

| Path | Purpose |
|---|---|
| `/opt/go-glpi-agent/go-glpi-agent` | the binary |
| `/opt/go-glpi-agent/agent.cfg` | configuration (kept across upgrades) |
| `/lib/systemd/system/go-glpi-agent.{service,timer}` | hourly oneshot run |
| `/lib/systemd/system/go-glpi-agent-daemon.service` | long-running daemon (alternative) |

## Configure

Edit `/opt/go-glpi-agent/agent.cfg` (INI format, compatible with the Perl agent's
`agent.cfg`). The minimum is a target:

```ini
server = http://glpi.example.com/front/inventory.php
tag    = datacenter-1
```

CLI flags override the file. See the bundled `agent.cfg` for every supported key (target,
scheduling, TLS, proxy, logging, categories).

## Run as a service

### Linux (systemd)

```sh
# recommended: one inventory per hour (randomized delay)
sudo systemctl enable --now go-glpi-agent.timer

# alternative: a long-running daemon (interval from `delaytime`)
sudo systemctl enable --now go-glpi-agent-daemon.service
```

## CLI usage

The CLI uses subcommands (Cobra):

```sh
# send to GLPI 10+ (native JSON; auto-fallback to XML/PROLOG)
go-glpi-agent run --server http://glpi/front/inventory.php

# write the inventory locally as XML
go-glpi-agent run --local /tmp/inventory

# use a specific config file
go-glpi-agent run --conf-file /opt/go-glpi-agent/agent.cfg

# long-running daemon (periodic cycles)
go-glpi-agent daemon

# version
go-glpi-agent version
```

Global flags: `--server`, `--local`, `--conf-file`, `--debug`, `--force`,
`--no-category`.

Tip: set `GFI_DUMP_JSON=<file>` to also write the native GLPI JSON to disk on a
`run --local`/`run --server`, so it can be validated offline against GLPI's
`inventory.schema.json`.

## Windows

The Windows build covers the same collector set, sourcing data from `gopsutil` plus WMI
(`Win32_*` classes) and the registry. Install from an elevated PowerShell:

```powershell
# extract go-glpi-agent_*_windows_amd64.zip, then in that folder:
.\install.ps1                    # copies the binary, seeds the config, schedules an hourly run
notepad C:\ProgramData\go-glpi-agent\agent.cfg   # set the `server` line
.\go-glpi-agent.exe run --debug  # send once now

.\uninstall.ps1                  # remove (keeps config/state; -Purge to wipe)
```

The installer registers a **Scheduled Task** (hourly, as SYSTEM) — the Windows analog of
the Linux systemd timer. The binary, config and state live under
`C:\Program Files\go-glpi-agent` and `C:\ProgramData\go-glpi-agent`.

Installed software is read from the uninstall registry keys (not `Win32_Product`, which is
slow and triggers MSI self-repair). Under the SYSTEM Scheduled Task, machine-wide software
is complete but other users' per-user `HKCU` installs are not enumerated.

## FreeBSD

The FreeBSD build covers the same collector set via `gopsutil` plus FreeBSD-native
sources. Extract `go-glpi-agent_*_freebsd_amd64.tar.gz` and follow the bundled
`INSTALL.md`:

```sh
sudo install -m 0755 go-glpi-agent /opt/go-glpi-agent/go-glpi-agent
sudo cp -n agent.cfg /opt/go-glpi-agent/agent.cfg   # set the `server` line
sudo /opt/go-glpi-agent/go-glpi-agent run           # once now
# or the rc.d service (daemon) / a cron entry for periodic runs — see INSTALL.md
```

## Collectors

| Category | Linux source | Windows source | FreeBSD source |
|---|---|---|---|
| CPU | gopsutil/cpu | WMI Win32_Processor | gopsutil/cpu, hw.model |
| Memory (+ slots) | gopsutil/mem, dmidecode | gopsutil/mem, WMI Win32_PhysicalMemory | gopsutil/mem |
| BIOS/board/chassis | /sys/class/dmi, dmidecode | WMI Win32_BIOS/ComputerSystem/BaseBoard | kenv smbios.* |
| Physical disks | lsblk | WMI Win32_DiskDrive | geom disk list |
| Filesystems | gopsutil/disk | gopsutil/disk | gopsutil/disk (UFS/ZFS) |
| LVM | lvs | — (n/a) | — (n/a) |
| USB | /sys/bus/usb | WMI Win32_PnPEntity | usbconfig |
| Network | gopsutil/net, /proc/net/route | gopsutil/net, WMI Win32_NetworkAdapter | gopsutil/net, route |
| OS / distro | gopsutil/host, /etc/os-release | gopsutil/host, registry CurrentVersion | gopsutil/host, sysctl |
| Hostname / domain | gopsutil/host, /etc | gopsutil/host | gopsutil/host, /etc |
| Timezone | /etc/timezone, /etc/localtime | registry TimeZoneKeyName | /var/db/zoneinfo |
| Users / groups | /etc/passwd, /etc/group, who, last | WMI Win32_UserAccount/Group | /etc/passwd, /etc/group, who, last |
| Processes (`scan-processes=1`) | gopsutil/process | gopsutil/process | gopsutil/process |
| Software | dpkg-query, rpm, pacman | registry Uninstall keys | pkg query |

Junk identity values (a serial of `0`, `None`, `To be filled by O.E.M.`, …) are filtered
out on every platform. On VirtualBox VMs, where the DMI/SMBIOS serial is `0`, the system
UUID is used as the serial (matching glpi-agent), so the host still gets a stable identity
in GLPI.

## Gaps vs the Perl agent

GPU, monitors (EDID), printers, PCI controllers; IPMI and RAID controllers;
Snap/Flatpak/Nix/Gentoo software; firewall, batteries, SSH keys, environment variables;
NetDiscovery, NetInventory, Deploy, WakeOnLan, ESX; macOS, other BSDs, AIX, Solaris; an
MSI installer and `windows/arm64`.

## Tested distributions

The native send to GLPI 10 and local collection are validated on: Rocky 9, RHEL 8/9,
CentOS Stream 10, AlmaLinux 8/9, Oracle Linux 8/9, Fedora 42, Debian 12/13,
Ubuntu 24.04/26.04, Pop!_OS 20.04, openSUSE Leap 15, and Arch Linux; Windows Server 2022;
FreeBSD 14.1.

Integration infrastructure lives in `test/` (GLPI 10 via Docker Compose, and per-OS
Vagrant matrices). See [test/README.md](../test/README.md).

## Build from source

Requires Go 1.26+.

```sh
make build           # local binary ./go-glpi-agent
make build-all       # static linux/amd64 in dist/
make build-windows   # static windows/amd64 (dist/go-glpi-agent.exe)
make package-windows # Windows .zip (exe + agent.cfg + install/uninstall.ps1)
make build-freebsd   # static freebsd/amd64 (dist/go-glpi-agent-freebsd)
make package-freebsd # FreeBSD .tar.gz (binary + agent.cfg + rc.d + INSTALL.md)
make test            # go test ./...
make package-deb     # .deb (requires nfpm)
make package-rpm     # .rpm (requires nfpm)
make package-arch    # Arch .pkg.tar.zst (requires nfpm)
make packages        # all three Linux packages at once
```

Module/repository: `go-glpi-agent`. The version is the git tag (`make build` bakes it via
ldflags); pushing a `v*` tag triggers the release workflow (`.github/workflows/release.yml`).

## Architecture

The codebase is split per-OS with build tags so each platform's binary carries only its
own collectors:

- `internal/collector/linux/` — `//go:build linux` (`/sys`, `/proc`, dmidecode, lsblk, lvs)
- `internal/collector/windows/` — `//go:build windows` (WMI, registry)
- `internal/collector/freebsd/` — `//go:build freebsd` (kenv, pkg, geom, sysctl, usbconfig)
- `internal/collector/generic/` — cross-platform collectors

Each OS is registered from `internal/agent/register_<goos>.go`, so adding macOS/another BSD
is a sibling package plus one registration file. The data model in `internal/inventory` is
the single source of truth; the XML and JSON serializers both read from it.

## Notes

- Some fields (memory slots via `dmidecode`, disk serials) require root.
- The device ID follows the Perl format (`{hostname}-{timestamp}`) and the agent imports an
  existing `FusionInventory-Agent.dump` / `GLPI-Agent.dump` on first run, so GLPI does not
  treat the machine as a new asset. A separate `agentid` (UUID v4) is sent in the
  `GLPI-Agent-ID` header.
