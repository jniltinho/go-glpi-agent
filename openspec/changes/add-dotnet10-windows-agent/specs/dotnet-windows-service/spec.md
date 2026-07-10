## ADDED Requirements

### Requirement: Native Windows Service hosting
The same executable used for one-shot commands SHALL run as a real Windows Service through the .NET Generic Host and Windows Service lifetime. It SHALL integrate with Service Control Manager without a wrapper executable or Scheduled Task.

#### Scenario: Start through Service Control Manager
- **WHEN** Service Control Manager starts the installed agent service
- **THEN** the process reports running status, initializes protected configuration/state, and enters periodic inventory scheduling

### Requirement: Non-overlapping periodic execution
The service SHALL apply a configurable randomized initial delay and inventory interval, execute at most one cycle at a time, and calculate the next cycle only after the current cycle reaches a terminal state.

#### Scenario: Collection exceeds the interval
- **WHEN** an inventory cycle lasts longer than the configured interval
- **THEN** the service does not start an overlapping cycle and schedules the next run after the current cycle completes

### Requirement: Graceful service lifecycle
The service SHALL handle start, stop, shutdown, and cancellation notifications, propagate cancellation to collectors and transport, stop accepting new cycles, and report stopped status within a configured grace period.

#### Scenario: Stop during collection
- **WHEN** Service Control Manager requests stop during an active collection
- **THEN** the service cancels the active cycle, releases Windows resources, persists no partial identity state, and stops within the grace period

### Requirement: Service resilience and recovery
The installed service SHALL use delayed automatic start and configured Service Control Manager recovery actions for unexpected process failure. Expected inventory or server errors MUST be logged and scheduled normally rather than crashing the service.

#### Scenario: Collector returns an expected error
- **WHEN** a collector reports access denied for one category
- **THEN** the service records the partial result, completes the cycle policy, remains running, and does not trigger SCM recovery

#### Scenario: Process terminates unexpectedly
- **WHEN** the service process exits unexpectedly
- **THEN** Service Control Manager applies the configured restart recovery action

### Requirement: Windows service logging
The service SHALL write lifecycle and high-severity events to Windows Event Log and detailed bounded diagnostic logs under the protected data directory. Log rotation or retention MUST prevent unbounded disk growth.

#### Scenario: Submission repeatedly fails
- **WHEN** several scheduled submissions fail
- **THEN** correlated failure summaries appear in Event Log, detailed redacted diagnostics appear in the rolling file, and retention limits are enforced

### Requirement: Protected service execution context
The service SHALL run under the configured machine service identity with read access required for inventory and modify access only to its protected data directory. Paths or files writable by unprivileged users MUST NOT be loaded as executable code or trusted configuration.

#### Scenario: Unprivileged user modifies an external file
- **WHEN** an unprivileged user changes a file outside the protected installation and data directories
- **THEN** the service neither loads that file as code/configuration nor changes its execution behavior

