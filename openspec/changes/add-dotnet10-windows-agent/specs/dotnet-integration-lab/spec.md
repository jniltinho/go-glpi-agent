## ADDED Requirements

### Requirement: Self-contained project test laboratory
All .NET-agent-specific Docker Compose, Vagrant, provisioning, fixture, orchestration, and result files SHALL live under `dotnet-glpi-agent/test/` and SHALL NOT depend on mutable files inside either Perl reference tree.

#### Scenario: Locate test infrastructure
- **WHEN** a test operator checks out the repository and enters `dotnet-glpi-agent/test/`
- **THEN** documented commands identify every required generated artifact and can run the laboratory without modifying the root test infrastructure

### Requirement: Reproducible GLPI Docker stack
The laboratory SHALL use pinned official GLPI and supported MariaDB images, provide health/readiness checks, persist isolated test volumes, expose the inventory endpoint to the VM, and automate initial installation, inventory enablement, and cache clearing.

#### Scenario: Start an empty GLPI environment
- **WHEN** the operator runs the documented GLPI start command with no existing volumes
- **THEN** Docker Compose pulls/starts the configured images, waits until GLPI is ready, enables native inventory non-interactively, and reports the reachable endpoint

### Requirement: Supported GLPI version matrix
The laboratory SHALL exercise native protocol and schema behavior against pinned GLPI 10 and GLPI 11 configurations when supported image tags are available. Each run SHALL record exact image tags and digests.

#### Scenario: Run the GLPI 10 and 11 matrix
- **WHEN** the operator requests the full protocol matrix
- **THEN** isolated GLPI 10 and GLPI 11 runs validate their own schema and asset assertions and record the image digests used

### Requirement: Windows Vagrant baseline
The laboratory SHALL define a Windows Server 2022 Vagrant baseline using WinRM, with documented VirtualBox and Hyper-V provider settings, configurable CPU/memory, and host-to-guest access to the GLPI endpoint. Additional Windows versions MAY extend but MUST NOT replace the baseline.

#### Scenario: Provision the baseline VM
- **WHEN** a prepared test host runs `vagrant up`
- **THEN** Vagrant creates the Windows Server 2022 VM, connects over WinRM, stages the test artifacts, and invokes PowerShell provisioning without manual guest interaction

### Requirement: End-to-end MSI and service validation
Vagrant provisioning SHALL silently install the development MSI, verify files/ACLs/configuration, start the Windows Service, run local JSON/XML inventory, submit inventory to GLPI, and collect service/Event Log/application logs.

#### Scenario: Provision and submit inventory
- **WHEN** the Windows VM is provisioned against the ready GLPI stack
- **THEN** the MSI installs successfully, the service reaches running state, local outputs are produced, and a server submission completes with correlated logs

### Requirement: Exact GLPI schema validation
The test workflow SHALL obtain the active `inventory.schema.json` from the GLPI container being tested and validate the .NET agent's native JSON against that exact file before or alongside server submission.

#### Scenario: Schema rejects a typed field
- **WHEN** a test fixture serializes a field with a type or enumeration not allowed by the active GLPI schema
- **THEN** offline schema validation fails with the precise JSON path and the end-to-end test is marked failed

### Requirement: GLPI asset acceptance assertions
After submission, the laboratory SHALL query a supported GLPI API or test database path for the Computer identified by the submitted device/agent identity and SHALL verify core OS, BIOS, CPU, memory, storage, network, and software values.

#### Scenario: Asset appears in GLPI
- **WHEN** the agent receives a successful native submission response
- **THEN** the test waits within a bounded period for exactly one matching Computer asset and validates the required high-value fields

### Requirement: Reference-agent comparison
When staged, the workflow SHALL run the official GLPI Agent and the root Go Windows agent on the same VM, collect local outputs, and generate a per-category comparison of stable fields, counts, omissions, and source differences. Acceptance MUST use documented minimum coverage and stable-field rules rather than exact transient count equality.

#### Scenario: Compare three agents
- **WHEN** .NET, official Perl, and Go artifacts are available in the VM
- **THEN** one machine-readable report highlights category coverage and stable-field differences for all three without treating expected transient rows as failures

### Requirement: MSI lifecycle acceptance tests
The Vagrant workflow SHALL test clean install, silent properties, service start/stop, repair, v1-to-v2 major upgrade, downgrade blocking, normal uninstall/reinstall preservation, explicit purge, and injected rollback before the first production MSI release.

#### Scenario: Exercise upgrade lifecycle
- **WHEN** the lifecycle suite installs v1, records identity/configuration, and upgrades to v2
- **THEN** v2 is the only registered product, the service is healthy, identity/configuration are preserved, and downgrade behavior is verified

### Requirement: Controlled orchestration and cleanup
The laboratory SHALL provide documented scripts to build artifacts, start/stop GLPI, provision/destroy Vagrant, select stages, keep resources for debugging, and collect machine-readable results. Default cleanup MUST remove ephemeral VMs and containers while protecting explicitly retained logs and reports.

#### Scenario: End-to-end test fails
- **WHEN** a provisioning or assertion step fails
- **THEN** the workflow records the failing stage and diagnostics, follows the requested keep-or-clean policy, and returns a nonzero status

### Requirement: CI and test-host separation
Hosted CI SHALL run restore, formatting/analyzers, unit and contract tests, `win-x64` publish, schema fixtures, and MSI build validation. The full Docker/Vagrant suite SHALL be an explicit manual or self-hosted job with documented virtualization and Windows licensing prerequisites.

#### Scenario: Run a pull-request build
- **WHEN** CI evaluates a pull request on ordinary hosted runners
- **THEN** deterministic build/test/package checks run without attempting to start the heavyweight Windows Vagrant VM

