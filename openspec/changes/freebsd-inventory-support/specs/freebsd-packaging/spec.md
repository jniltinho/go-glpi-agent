## ADDED Requirements

### Requirement: The build SHALL produce a FreeBSD distribution

The `Makefile` and release workflow SHALL build a static `freebsd/amd64` binary
(`CGO_ENABLED=0 GOOS=freebsd GOARCH=amd64`) and package it as a tarball containing the
binary, an `agent.cfg`, an `rc.d` service script and a `periodic`/cron entry for scheduled
runs. The artifact SHALL be produced on the existing Linux release runner.

#### Scenario: Release produces the FreeBSD tarball

- **WHEN** a `v*` tag is pushed and the release workflow runs
- **THEN** the GitHub release includes `go-glpi-agent_<version>_freebsd_amd64.tar.gz` alongside the Linux and Windows artifacts, listed in `checksums.txt`

#### Scenario: Compile check in CI

- **WHEN** the `go.yml` CI workflow runs
- **THEN** it runs `GOOS=freebsd go build ./...` and fails if the FreeBSD build breaks

### Requirement: The agent SHALL run as a scheduled service on FreeBSD

The distribution SHALL provide an `rc.d` script to run the agent and a `periodic`/cron entry
that triggers a periodic inventory (the FreeBSD analog of the systemd timer), installing the
binary and config under a documented prefix without clobbering an existing `agent.cfg`.

#### Scenario: Scheduled run configured

- **WHEN** an administrator installs the tarball and enables the service
- **THEN** the agent runs on a periodic schedule and a `run` succeeds against the configured target

### Requirement: FreeBSD inventory SHALL be validated against GLPI and the official agent

The repository SHALL provide Vagrant infrastructure (`test/vagrant-freebsd/`) using an
official `freebsd/FreeBSD-14` box that installs the agent, sends an inventory to the GLPI 10
docker stack in `test/glpi/`, and compares the per-section output against the official
FusionInventory agent (`pkg install fusioninventory-agent`). The native JSON SHALL pass
GLPI's `inventory.schema.json`.

#### Scenario: FreeBSD VM sends a valid inventory

- **WHEN** `vagrant up` provisions the FreeBSD box and runs `go-glpi-agent run --server <glpi>`
- **THEN** GLPI accepts the inventory (HTTP 200) and a FreeBSD computer asset appears with CPU, memory, disks, network and software populated

#### Scenario: Comparison against the official agent

- **WHEN** the provisioner also runs the official `fusioninventory-agent` locally
- **THEN** the per-section item counts of both agents are reported side by side, and the core hardware sections match

#### Scenario: Schema validation offline

- **WHEN** the provisioner runs `run` with `GFI_DUMP_JSON` set to a file
- **THEN** the dumped JSON validates against GLPI's `inventory.schema.json`
