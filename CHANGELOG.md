# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[Semantic Versioning](https://semver.org/). The version is the git tag.

## [Unreleased]

—

## [0.1.2] — 2026-06-30

### Changed
- docs: polish the `create-release` skill — adopt the clearer structure
  (release-approaches table, commit→CHANGELOG categorization, monitor/verify
  checklists, workflow-capabilities and adapting tables) from the go-postfixadmin
  skill, while keeping this project's CHANGELOG-driven CI publishing, nfpm
  `.deb`/`.rpm`/Arch packaging, and YAML frontmatter for skill discovery.

## [0.1.1] — 2026-06-30

### Changed
- CI: bump GitHub Actions to Node 24 (`actions/checkout@v5`, `actions/setup-go@v6`)
  and publish releases with the native `gh` CLI, removing the last Node 20 action
  and an external dependency.
- Release notes are now written from `CHANGELOG.md` (`--notes-file`) instead of
  being auto-generated from commits.

## [0.1.0] — 2026-06-29

First release: a Go reimplementation of the FusionInventory/GLPI inventory agent
for Linux, distinct from the Perl `fusioninventory-agent` and the official
`glpi-agent` so the three can coexist.

### Added
- **GLPI 10+ native protocol** (primary): CONTACT probe, JSON inventory to
  `/front/inventory.php`, `GLPI-Agent-ID` (UUID v4) header, zlib compression
  (or none). Validated against a real GLPI 10 with zero `inventory.schema.json`
  violations.
- **Legacy fallback**: automatic XML/PROLOG when the server is not native.
- **Linux collectors**: CPU, memory (+ dmidecode slots), BIOS/DMI, physical
  disks (lsblk), filesystems, LVM, USB, network, OS/distro, hostname, timezone,
  users/groups/logged-in, processes, and software (dpkg/rpm/pacman).
- **Cobra CLI** with `run`, `daemon`, and `version` subcommands.
- **Config**: reads the Perl agent's `agent.cfg` (INI), installed at
  `/opt/go-glpi-agent/agent.cfg`.
- **systemd**: oneshot `.service` + hourly `.timer`, plus an optional daemon
  unit; everything installs under `/opt/go-glpi-agent`.
- **Packaging**: `.deb`, `.rpm`, Arch `.pkg.tar.zst` (nfpm) and a `.tar.gz`,
  plus a GitHub Actions release workflow on `v*` tags.
- **Identity/migration**: persistent device ID in the Perl format and a separate
  `agentid` UUID; imports an existing `FusionInventory-Agent.dump` /
  `GLPI-Agent.dump` on first run.
- DMI junk-value filtering (serials of `0`, `None`, `To be filled by O.E.M.`, …).
- Validated across 16 Linux distributions (RHEL/Rocky/Alma/Oracle 8–9, CentOS
  Stream 10, Fedora 42, Debian 12/13, Ubuntu 24.04/26.04, Pop!_OS 20.04,
  openSUSE Leap 15, Arch Linux).
