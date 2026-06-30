# macos-software Specification

## Purpose
TBD - created by archiving change 2026-06-30-macos-inventory-support. Update Purpose after archive.
## Requirements
### Requirement: The agent SHALL collect installed macOS applications

On `GOOS=darwin` the agent SHALL populate the SOFTWARES section from
`system_profiler SPApplicationsDataType`, recording each application's name, version,
publisher and install date with `FROM = system_profiler`. The collector SHALL degrade to
an empty section (without aborting the inventory) when the data is unavailable.

#### Scenario: Applications listed

- **WHEN** the agent runs on macOS with applications installed
- **THEN** SOFTWARES contains entries with name and version (and publisher/install date when available), each tagged `FROM = system_profiler`

#### Scenario: Graceful when unavailable

- **WHEN** `system_profiler SPApplicationsDataType` fails or returns nothing
- **THEN** the software section is empty and the rest of the inventory is unaffected

