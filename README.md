# go-glpi-agent

A Go reimplementation of the FusionInventory/GLPI inventory agent, focused on
**Linux**. It produces a single static binary with no runtime dependencies and
talks to **GLPI 10+** using the native JSON protocol, with automatic fallback to
the legacy OCS/FusionInventory XML protocol.

The name is intentionally distinct from the Perl `fusioninventory-agent` and the
official `glpi-agent`, so the three can coexist on the same host.

## Status

v1 — Linux/amd64. Collects local hardware and software inventory and sends it to
GLPI 10+ (native JSON) or writes it to a local XML file. Validated against a real
GLPI 10 and across 16 Linux distributions (see [Tested distributions](#tested-distributions)).

## Install

Download a release artifact from
[Releases](https://github.com/jniltinho/go-glpi-agent/releases), or build from
source (see [Build](#build)). Everything installs under `/opt/go-glpi-agent`.

```sh
# Debian / Ubuntu
sudo dpkg -i go-glpi-agent_*_amd64.deb

# RHEL / Rocky / Alma / Fedora / openSUSE
sudo rpm -i go-glpi-agent-*.x86_64.rpm

# or the portable tarball
tar -xzf go-glpi-agent_*_linux_amd64.tar.gz -C /opt/go-glpi-agent
```

The packages install:

| Path | Purpose |
|---|---|
| `/opt/go-glpi-agent/go-glpi-agent` | the binary |
| `/opt/go-glpi-agent/agent.cfg` | configuration (kept across upgrades) |
| `/lib/systemd/system/go-glpi-agent.{service,timer}` | hourly oneshot run |
| `/lib/systemd/system/go-glpi-agent-daemon.service` | long-running daemon (alternative) |

## Configure

Edit `/opt/go-glpi-agent/agent.cfg` (INI format, compatible with the Perl
agent's `agent.cfg`). The minimum is a target:

```ini
server = http://glpi.example.com/front/inventory.php
tag    = datacenter-1
```

CLI flags override the file. See `agent.cfg` for every supported key (target,
scheduling, TLS, proxy, logging, categories).

## Run as a service (systemd)

```sh
# recommended: one inventory per hour (randomized delay)
sudo systemctl enable --now go-glpi-agent.timer

# alternative: a long-running daemon (interval from `delaytime`)
sudo systemctl enable --now go-glpi-agent-daemon.service
```

## Usage (CLI)

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

## Collectors (v1)

| Category | Source | Status |
|---|---|---|
| CPU | gopsutil/cpu | ✅ |
| Memory + slots | gopsutil/mem, dmidecode | ✅ |
| BIOS/DMI | /sys/class/dmi, dmidecode | ✅ |
| Physical disks | lsblk | ✅ |
| Filesystems | gopsutil/disk | ✅ |
| LVM | lvs | ✅ |
| USB | /sys/bus/usb | ✅ |
| Network | gopsutil/net, /proc/net/route | ✅ |
| OS / distro | gopsutil/host, /etc/os-release | ✅ |
| Hostname / domain | gopsutil/host, /etc | ✅ |
| Timezone | /etc/timezone, /etc/localtime | ✅ |
| Users / groups / logged-in | /etc/passwd, /etc/group, who, last | ✅ |
| Processes (`scan-processes=1`) | gopsutil/process | ✅ |
| Software dpkg/rpm/pacman | dpkg-query, rpm, pacman | ✅ |

Junk DMI values (a serial of `0`, `None`, `To be filled by O.E.M.`, …) are
filtered out, so they are not reported as real data.

### Gaps vs the Perl agent (planned for v2)

GPU, monitors (EDID), printers, PCI controllers; IPMI and RAID controllers;
Snap/Flatpak/Nix/Gentoo software; firewall, batteries, SSH keys, environment
variables; NetDiscovery, NetInventory, Deploy, WakeOnLan, ESX; Windows, macOS,
BSD, AIX, Solaris.

## Tested distributions

The native send to GLPI 10 and local collection are validated on:
Rocky 9, RHEL 8/9, CentOS Stream 10, AlmaLinux 8/9, Oracle Linux 8/9, Fedora 42,
Debian 12/13, Ubuntu 24.04/26.04, Pop!_OS 20.04, openSUSE Leap 15, and Arch Linux.

Integration infrastructure lives in `test/` (GLPI 10 via Docker Compose, and a
multi-distro Vagrant matrix). See `test/README.md`.

## Build

Requires Go 1.26+.

```sh
make build          # local binary ./go-glpi-agent
make build-all      # static linux/amd64 in dist/
make test           # go test ./...
make package-deb    # .deb (requires nfpm)
make package-rpm    # .rpm (requires nfpm)
```

Module/repository: `go-glpi-agent`. The version is the git tag (`make build`
bakes it via ldflags); pushing a `v*` tag triggers the release workflow
(`.github/workflows/release.yml`).

## Notes

- Some fields (memory slots via `dmidecode`, disk serials) require root.
- The device ID follows the Perl format (`{hostname}-{timestamp}`) and the
  agent imports an existing `FusionInventory-Agent.dump` / `GLPI-Agent.dump` on
  first run, so GLPI does not treat the machine as a new asset. A separate
  `agentid` (UUID v4) is sent in the `GLPI-Agent-ID` header.

## License

GPL-2.0-or-later.
