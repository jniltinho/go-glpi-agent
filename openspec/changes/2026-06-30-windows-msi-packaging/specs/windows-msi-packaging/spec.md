## ADDED Requirements

### Requirement: The build SHALL produce a Windows MSI on the Linux runner

The `Makefile` and release workflow SHALL build a Windows `.msi` from a WiX source
(`contrib/windows/msi/go-glpi-agent.wxs`) using `wixl` (msitools) on the existing Linux
release runner — no Windows-only build tooling — producing
`go-glpi-agent_<version>_x64.msi`. The same `.wxs` SHALL also be buildable with WiX
(`wix build`) on a Windows host.

#### Scenario: Release produces the MSI

- **WHEN** a `v*` tag is pushed and the release workflow runs
- **THEN** the GitHub release includes `go-glpi-agent_<version>_x64.msi`, built on the Linux runner, alongside the existing Windows `.zip`, listed in `checksums.txt`

#### Scenario: MSI builds without Windows tooling

- **WHEN** `make package-msi` runs on the Linux runner with `wixl` installed
- **THEN** a valid installable `.msi` is produced (verifiable with `msiinfo`/`msiexec`)

### Requirement: The MSI SHALL install the agent and a scheduled inventory run

Installing the MSI SHALL place `go-glpi-agent.exe` under `%ProgramFiles%\go-glpi-agent`,
seed `agent.cfg` under `%ProgramData%\go-glpi-agent` without overwriting an existing
config, and register a `go-glpi-agent` Scheduled Task that runs `go-glpi-agent.exe run`
hourly as SYSTEM. The MSI SHALL support silent install (`/qn`) and public `SERVER` and
`TAG` properties that are written into a freshly seeded `agent.cfg`.

#### Scenario: Silent install configures and schedules the agent

- **WHEN** an administrator runs `msiexec /i go-glpi-agent_<ver>_x64.msi /qn SERVER=http://glpi/front/inventory.php TAG=dc1`
- **THEN** the install succeeds (exit 0), `%ProgramFiles%\go-glpi-agent\go-glpi-agent.exe` exists, `agent.cfg` under `%ProgramData%` contains the given `server`/`tag`, and a `go-glpi-agent` Scheduled Task runs hourly as SYSTEM

#### Scenario: Inventory succeeds after MSI install

- **WHEN** the installed agent runs an inventory against the configured GLPI server
- **THEN** the native JSON validates against GLPI's `inventory.schema.json` and GLPI accepts it

### Requirement: The MSI SHALL upgrade in place and uninstall cleanly

The MSI SHALL carry a stable `UpgradeCode` and a `MajorUpgrade` so installing a newer
version over an older one upgrades in place while preserving `agent.cfg` and the `var`
state (deviceid/agentid). Uninstalling SHALL remove the Scheduled Task and the binary by
product code; config and state SHALL be preserved unless `PURGE=1` is passed.

#### Scenario: Upgrade preserves config and identity

- **WHEN** a newer MSI is installed over an older one on a host that already has `agent.cfg` and `var`
- **THEN** the old version is removed first, the new binary is in place, and the existing config and deviceid/agentid are kept so GLPI does not create a duplicate asset

#### Scenario: Uninstall removes task and binary, keeps config

- **WHEN** an administrator runs `msiexec /x go-glpi-agent_<ver>_x64.msi /qn`
- **THEN** the `go-glpi-agent` Scheduled Task and the binary are removed, and `agent.cfg`/`var` remain

#### Scenario: Purge uninstall removes everything

- **WHEN** an administrator runs `msiexec /x go-glpi-agent_<ver>_x64.msi /qn PURGE=1`
- **THEN** the task, binary, config and `var` data directory are all removed

### Requirement: The MSI install/uninstall SHALL be validated on a Windows runner

CI SHALL validate the MSI end-to-end on `windows-latest`: install silently with
`msiexec /qn`, assert the Scheduled Task and binary exist, run an inventory and validate
the native JSON against GLPI's `inventory.schema.json`, then uninstall with `msiexec /x
/qn` and assert the task and binary are gone. The `.msi` SHALL be uploaded as a build
artifact.

#### Scenario: Install→verify→uninstall round-trip is green

- **WHEN** the Windows CI job installs the MSI, runs an inventory, and uninstalls
- **THEN** every step succeeds: install exit 0, task present, JSON schema-valid, and after uninstall the task and binary are absent

#### Scenario: MSI uploaded as artifact

- **WHEN** the Windows CI job finishes
- **THEN** the `.msi` is available as a downloadable build artifact
