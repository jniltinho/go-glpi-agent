# macos-os Specification

## Purpose
TBD - created by archiving change 2026-06-30-macos-inventory-support. Update Purpose after archive.
## Requirements
### Requirement: The agent SHALL collect the macOS operating system and kernel

On `GOOS=darwin` the agent SHALL populate the OPERATINGSYSTEM and HARDWARE sections
with the macOS product name and version, build, kernel (Darwin) version, architecture,
boot time, hostname/FQDN and timezone, sourced from `sw_vers`, `uname`, gopsutil/host,
and `system_profiler SPSoftwareDataType`.

#### Scenario: macOS OS section populated

- **WHEN** `go-glpi-agent run --local <dir>` runs on macOS
- **THEN** OPERATINGSYSTEM has a macOS product name (e.g. "macOS"), a product version (e.g. "14.5"), `KERNEL_NAME = Darwin`, a kernel version, an arch (`x86_64` or `arm64`), and a boot time
- **AND** HARDWARE carries the OS name/version and the machine hostname

#### Scenario: Architecture reported correctly on both CPUs

- **WHEN** the agent runs on an Intel Mac and on an Apple Silicon Mac
- **THEN** the reported arch is `x86_64` on Intel and `arm64` on Apple Silicon, and the value passes GLPI's schema enum after normalization

