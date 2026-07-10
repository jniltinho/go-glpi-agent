## ADDED Requirements

### Requirement: Per-machine self-contained MSI
The project SHALL build a versioned `win-x64` MSI that installs the self-contained application under `%ProgramFiles%\DotnetGlpiAgent` and protected configuration/state directories under `%ProgramData%\DotnetGlpiAgent`. The package MUST NOT require a preinstalled .NET runtime.

#### Scenario: Clean silent installation
- **WHEN** an administrator installs the MSI silently on a clean supported Windows VM
- **THEN** binaries, initial configuration, protected directories, uninstall registration, and the Windows Service are installed without interactive prompts

### Requirement: MSI-managed service lifecycle
The MSI SHALL install, configure, start, stop, and remove the Windows Service through Windows Installer service tables or reviewed WiX service elements. Service executable paths and arguments MUST be quoted and resolve only inside the protected installation directory.

#### Scenario: Uninstall a running service
- **WHEN** an administrator uninstalls the package while the service is running
- **THEN** Windows Installer stops and removes the service before deleting its binaries and reports no reboot requirement under normal conditions

### Requirement: Safe unattended configuration
The MSI SHALL expose documented public properties for non-secret fleet settings including server URL, tag, install directory, start-service behavior, and initial run behavior. Secret values MUST NOT be accepted through ordinary logged MSI properties and SHALL be provisioned through a protected post-install configuration path.

#### Scenario: Deploy server and tag silently
- **WHEN** an administrator installs with valid `SERVER` and `TAG` properties
- **THEN** first-install configuration contains those values and MSI logs contain no secret material

### Requirement: Configuration and identity preservation
First installation SHALL seed configuration only when absent. Repair, major upgrade, and normal uninstall MUST preserve user-edited configuration and persistent device/agent identity; an explicit documented `PURGE=1` operation SHALL remove preserved state.

#### Scenario: Upgrade an edited installation
- **WHEN** version 1 is configured and has generated identity before version 2 is installed as a major upgrade
- **THEN** version 2 retains the edited configuration and the same device and agent identifiers

#### Scenario: Normal uninstall and reinstall
- **WHEN** an administrator uninstalls without purge and later reinstalls
- **THEN** the reinstalled agent reuses the preserved configuration and identity

#### Scenario: Explicit purge
- **WHEN** an administrator invokes the documented purge removal as an elevated operation
- **THEN** binaries, service, configuration, identity, and retained state are removed and the destructive effect is clearly reported

### Requirement: Deterministic upgrade and downgrade behavior
The MSI SHALL use a stable product family identity, generate appropriate per-version product codes, perform transactional major upgrades, and block installation of an older version over a newer installed version.

#### Scenario: Major upgrade succeeds
- **WHEN** version 2 is installed over version 1
- **THEN** version 1 is removed transactionally, exactly one product/service remains, and version 2 starts with preserved state

#### Scenario: Downgrade is attempted
- **WHEN** version 1 is installed over version 2
- **THEN** Windows Installer blocks the downgrade with an actionable message and leaves version 2 operational

### Requirement: Repair and rollback safety
MSI repair SHALL restore missing owned binaries and service registration without overwriting mutable configuration. Failed install or upgrade SHALL roll back owned changes and MUST NOT leave an orphan service, partial executable directory, or destroyed prior configuration.

#### Scenario: Repair missing executable
- **WHEN** the installed executable is missing and an administrator runs MSI repair
- **THEN** the executable and service registration are restored while configuration and identity remain unchanged

#### Scenario: Upgrade fails after service stop
- **WHEN** a fault is injected after the previous service is stopped during upgrade
- **THEN** Windows Installer rolls back to a consistent prior or absent state according to the transaction and preserves mutable data

### Requirement: Minimal reviewed elevated actions
The installer SHALL prefer declarative MSI/WiX tables and extensions. Any elevated custom action MUST be justified, path-validated, conditioned separately for install/repair/upgrade/uninstall, impersonation-reviewed, and paired with rollback behavior.

#### Scenario: Installer security review
- **WHEN** the MSI authoring is inspected before release
- **THEN** every elevated action has documented necessity, conditions, validated inputs, rollback, and automated lifecycle coverage

### Requirement: Release artifacts and signing
The packaging pipeline SHALL emit the MSI, SHA-256 checksums, SBOM, dependency/license notices, and signing inputs. A production release MUST Authenticode-sign the executable and MSI with trusted timestamping; development builds SHALL be visibly identified as unsigned.

#### Scenario: Verify a production package
- **WHEN** a release MSI is downloaded and verified
- **THEN** its checksum matches, executable and MSI signatures are valid and timestamped, and SBOM/notices correspond to the packaged version

### Requirement: MSI toolchain approval gate
The project MUST record acceptance of the WiX Toolset 7 OSMF EULA and any applicable maintenance-fee obligation before release packaging. If the terms are not accepted, an approved MSI toolchain SHALL replace WiX before installer implementation proceeds.

#### Scenario: Packaging decision is unresolved
- **WHEN** no recorded WiX licensing decision exists
- **THEN** release MSI work remains blocked and no pipeline silently accepts the EULA on behalf of the distributor

