## ADDED Requirements

### Requirement: The build SHALL produce a Windows distribution

The `Makefile` and release workflow SHALL build a static `go-glpi-agent.exe`
(`CGO_ENABLED=0 GOOS=windows GOARCH=amd64`) and package it as a `.zip` containing the
binary, a Windows `agent.cfg`, and `install.ps1`/`uninstall.ps1`. The artifact SHALL be
produced on the existing Linux release runner with no Windows-only tooling.

#### Scenario: Release produces the zip

- **WHEN** a `v*` tag is pushed and the release workflow runs
- **THEN** the GitHub release includes `go-glpi-agent_<version>_windows_amd64.zip` alongside the existing Linux artifacts, listed in `checksums.txt`

#### Scenario: Compile check in CI

- **WHEN** the `go.yml` CI workflow runs on a pull request
- **THEN** it runs `GOOS=windows go build ./...` and fails if the Windows build breaks

### Requirement: The installer SHALL configure a scheduled inventory run

`install.ps1` SHALL place the binary under `%ProgramFiles%\go-glpi-agent`, write
`agent.cfg` under `%ProgramData%\go-glpi-agent` without overwriting an existing config, and
register a Scheduled Task that runs `go-glpi-agent.exe run` on an hourly schedule (the
Windows analog of the systemd timer). `uninstall.ps1` SHALL remove the task and binary
while preserving the config and `var` state by default.

#### Scenario: Install registers the task

- **WHEN** an administrator runs `install.ps1`
- **THEN** a `go-glpi-agent` Scheduled Task exists, runs hourly as SYSTEM, and an immediate `run` succeeds against the configured target

#### Scenario: Upgrade preserves config

- **WHEN** `install.ps1` runs on a host that already has `agent.cfg`
- **THEN** the existing config and `var` state (deviceid/agentid) are kept so GLPI does not treat the host as a new asset

#### Scenario: Uninstall cleans up

- **WHEN** an administrator runs `uninstall.ps1`
- **THEN** the Scheduled Task and binary are removed; config/state remain unless a purge flag is passed

### Requirement: Windows inventory SHALL be validated end-to-end

The repository SHALL provide Vagrant infrastructure (`test/vagrant-windows/`) using a
`gusztavvargadr/windows-server-*` box, WinRM-provisioned to install the agent and send an
inventory to the GLPI 10 docker stack in `test/glpi/`. The native JSON payload SHALL pass
GLPI's `inventory.schema.json`.

#### Scenario: Windows VM sends a valid inventory

- **WHEN** `vagrant up` provisions the Windows box and runs `go-glpi-agent.exe run --server <glpi>`
- **THEN** GLPI accepts the inventory (HTTP 200) and a Windows computer asset appears with CPU, memory, disks, network and software populated

#### Scenario: Schema validation offline

- **WHEN** the provisioner runs `run` with `GFI_DUMP_JSON` set to a file
- **THEN** the dumped JSON validates against GLPI's `inventory.schema.json`
