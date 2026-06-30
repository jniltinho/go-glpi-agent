<p align="center">
  <img src="docs/assets/logo-horizontal.svg" alt="go-glpi-agent" width="440">
</p>

<p align="center">
  <b>One small binary that inventories Linux, Windows and FreeBSD into GLPI 10+</b><br>
  — no agent runtime, no Perl, no dependencies.
</p>

<p align="center">
  <a href="https://github.com/jniltinho/go-glpi-agent/releases"><img src="https://img.shields.io/github/v/release/jniltinho/go-glpi-agent?sort=semver" alt="Release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--2.0--or--later-blue" alt="License"></a>
  <a href="go.mod"><img src="https://img.shields.io/badge/Go-1.26%2B-00ADD8?logo=go" alt="Go"></a>
  <img src="https://img.shields.io/badge/platforms-Linux%20%7C%20Windows%20%7C%20FreeBSD-555" alt="Platforms">
</p>

<p align="center">
  <img src="docs/assets/banner.jpg" alt="go-glpi-agent — bridge your devices to GLPI with the power of Go" width="100%">
</p>

A Go reimplementation of the FusionInventory/GLPI inventory agent. It collects local
hardware and software inventory and sends it to a **GLPI 10+** server using the native
JSON protocol (with automatic fallback to the legacy OCS/FusionInventory XML), or writes
it to a local file.

## Why use it

- **🚀 Single static binary** — drop it in and run. No Perl, no modules, no `pkg` tree.
- **💻 One agent, three OSes** — Linux, Windows and FreeBSD from the same build.
- **🔌 Drop-in for GLPI** — reads the same `agent.cfg` as the Perl agent and reuses your
  existing device IDs, so hosts aren't re-created as new assets.
- **📦 Native packages** — `.deb`, `.rpm`, Arch `.pkg.tar.zst`, a Windows `.zip` installer,
  and a FreeBSD tarball — published on every release.
- **🪶 Lightweight & quiet** — runs on a schedule (systemd timer / Scheduled Task / cron),
  collects in parallel, and stays out of the way.

## Quick start

**1. Install** (grab a package from [Releases](https://github.com/jniltinho/go-glpi-agent/releases)):

```sh
# Debian / Ubuntu
sudo dpkg -i go-glpi-agent_*_amd64.deb
# RHEL / Rocky / Alma / Fedora / openSUSE
sudo rpm -i go-glpi-agent-*.x86_64.rpm
# Arch
sudo pacman -U go-glpi-agent-*-x86_64.pkg.tar.zst
```

Windows: extract `…_windows_amd64.zip` and run `install.ps1` (elevated).
FreeBSD: extract `…_freebsd_amd64.tar.gz` and follow `INSTALL.md`.

**2. Point it at your GLPI** — edit `/opt/go-glpi-agent/agent.cfg`:

```ini
server = http://glpi.example.com/front/inventory.php
tag    = datacenter-1
```

**3. Schedule it** (one inventory per hour):

```sh
sudo systemctl enable --now go-glpi-agent.timer     # Linux
# Windows: install.ps1 already registered a Scheduled Task
# FreeBSD: enable the rc.d service or add a cron entry (see INSTALL.md)
```

That's it — a computer asset appears in GLPI on the next run. Want to test first?
`go-glpi-agent run --local /tmp/inv` writes the inventory as XML without sending anything.

## Supported platforms

| OS | Arch | Inventory sources |
|---|---|---|
| **Linux** (16 distros) | amd64 | `/sys`, `/proc`, dmidecode, lsblk, lvs, dpkg/rpm/pacman |
| **Windows** 10/11 / Server | amd64 | WMI (`Win32_*`) + registry |
| **FreeBSD** 14 | amd64 | kenv (smbios), pkg, geom, sysctl, usbconfig |

Validated against a real GLPI 10 across all three. The full collector matrix, per-OS
install details, CLI/config reference, build instructions and architecture live in
**[docs/REFERENCE.md](docs/REFERENCE.md)**.

## Docs

- 📘 **[Full reference](docs/REFERENCE.md)** — collectors, install per OS, CLI, config, build, internals
- 📝 [Changelog](CHANGELOG.md) · 🤝 [Contributing](CONTRIBUTING.md) · 🧪 [Test infrastructure](test/README.md)

## License

GPL-2.0-or-later.
