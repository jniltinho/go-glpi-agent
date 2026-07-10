## ADDED Requirements

### Requirement: Independent .NET 10 project
The system SHALL be implemented under `dotnet-glpi-agent/` as an independent .NET 10 solution with its own source, tests, packaging, documentation, dependency pins, and build outputs. Building or testing this solution MUST NOT compile or modify the root Go module or either `base/` reference tree.

#### Scenario: Build the isolated solution
- **WHEN** a developer restores, builds, and tests the solution from `dotnet-glpi-agent/`
- **THEN** the operation succeeds without invoking the Go or Perl toolchains and writes only inside the .NET project or configured artifact directories

### Requirement: Compatibility-first Windows publishing
The application SHALL target `net10.0-windows` and SHALL produce a self-contained `win-x64` publish output that does not require a separately installed .NET runtime. Trimming and Native AOT MUST remain disabled until a dedicated compatibility test proves all Windows management, service, configuration, and serialization paths.

#### Scenario: Run on a clean Windows VM
- **WHEN** the published application is copied to a supported Windows VM without a machine-wide .NET runtime
- **THEN** `dotnet-glpi-agent.exe version` and a local inventory run complete successfully

### Requirement: Stable command surface
The executable SHALL provide `run`, `validate-config`, and `version` commands with documented nonzero exit codes for invalid configuration, collection failure that prevents a usable inventory, transport failure, and unexpected internal failure. The `run` command SHALL support local output, server submission, forced submission, category exclusion, and debug diagnostics.

#### Scenario: Reject invalid invocation
- **WHEN** a user invokes a command with an unknown option or a required target is missing
- **THEN** the process prints concise usage information to stderr and exits with the documented invalid-invocation code

#### Scenario: Produce a local inventory
- **WHEN** a user runs the agent with a valid local output target
- **THEN** the process collects one inventory snapshot, writes the selected output format atomically, reports the resulting path, and exits successfully

### Requirement: Layered compatible configuration
The application SHALL load defaults, `agent.cfg` and included configuration files, `GLPI_AGENT_*` environment variables, and CLI options in that ascending precedence order. It SHALL support the commonly used Perl/Go inventory, scheduling, category, proxy, authentication, TLS, compression, and logging keys, and SHALL warn about unknown keys without logging their values.

#### Scenario: CLI overrides file and environment
- **WHEN** the same server setting is present in `agent.cfg`, an environment variable, and a CLI option
- **THEN** the CLI value is used and the effective configuration report identifies the winning source without exposing credentials

#### Scenario: Validate configuration without collecting
- **WHEN** a user runs `validate-config` against a syntactically valid file containing an unknown key
- **THEN** the command reports the unknown key as a warning, validates all known settings, performs no inventory collection or network submission, and exits successfully

### Requirement: Persistent agent identity
The system SHALL generate cryptographically random persistent agent and device identifiers on first use, store them atomically under the protected state directory, and reuse them across process restarts. Corrupt or partially written state MUST be reported and recovered without silently changing an otherwise valid identifier.

#### Scenario: Reuse identity after restart
- **WHEN** the agent completes a run, stops, and is started again with the same state directory
- **THEN** the second run uses the same agent ID and device ID

### Requirement: Single source inventory model
The system SHALL represent collected data in a typed protocol-neutral inventory model. Native JSON, legacy XML, local output, and comparison reports MUST consume the same immutable inventory snapshot rather than recollecting data or maintaining separate source models.

#### Scenario: Serialize one snapshot twice
- **WHEN** one collected snapshot is serialized as native JSON and legacy XML
- **THEN** equivalent categories and identifiers originate from the same typed values and collection timestamps

### Requirement: Isolated collector orchestration
The system SHALL execute enabled collectors with bounded concurrency, per-collector deadlines, cancellation, and structured collection results. Failure or timeout of one collector MUST NOT discard successful unrelated categories, and no timed-out adapter work may be abandoned without observation and resource cleanup.

#### Scenario: One collector times out
- **WHEN** a collector exceeds its configured deadline while other collectors complete
- **THEN** the inventory contains the successful categories, the timed-out category is marked incomplete in the report, resources are disposed, and the run follows the configured partial-inventory policy

### Requirement: Safe diagnostics
The application SHALL provide structured console, file, and host-integrated logging with correlation IDs and collection summaries. Passwords, tokens, private keys, sensitive registry values, and authorization headers MUST be redacted at every log level.

#### Scenario: Debug an authenticated submission
- **WHEN** debug logging is enabled for a submission using credentials
- **THEN** request metadata and correlation IDs are logged while all credential material remains redacted

