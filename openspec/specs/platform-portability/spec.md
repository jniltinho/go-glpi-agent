# platform-portability Specification

## Purpose

Ensure the agent builds and runs correctly across both Linux and Windows, with
OS-aware defaults, per-platform logger backends, and per-platform collector
registration, without regressing the existing Linux behavior.

## Requirements

### Requirement: The module SHALL compile for Windows

The Go module SHALL build successfully with `GOOS=windows GOARCH=amd64 go build ./...`.
No package imported by a non-test build target may be POSIX-only on the Windows build.

#### Scenario: Cross-compile for Windows succeeds

- **WHEN** `CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build ./...` runs on the Linux CI host
- **THEN** the build completes without error and produces `go-glpi-agent.exe`

#### Scenario: Linux build is unaffected

- **WHEN** `go build ./...` runs on Linux
- **THEN** the build still succeeds and the resulting binary registers only the Linux collectors

### Requirement: Logger backends SHALL be split per platform

The logger SHALL provide the syslog backend only on non-Windows platforms. On Windows,
selecting `logger = Syslog` SHALL fall back to stderr (or the Windows event log) without
a compile error, and the `Stderr`/`File` backends SHALL behave identically on all platforms.

#### Scenario: Syslog requested on Windows

- **WHEN** the agent runs on Windows with `logger = Syslog`
- **THEN** the agent does not crash and logs are written to the fallback backend

#### Scenario: File backend on Windows

- **WHEN** the agent runs on Windows with `logger = File` and a writable `logfile`
- **THEN** log lines are appended to that file in the same `[level] message` format as on Linux

### Requirement: Default paths SHALL be OS-aware

`config.Default()` and the default configuration-file path SHALL resolve to platform-native
locations: under `%ProgramData%\go-glpi-agent` on Windows and under `/opt/go-glpi-agent`
on Linux. Persisted `deviceid`/`agentid` state SHALL be stored under the OS-appropriate `vardir`.

#### Scenario: Windows defaults

- **WHEN** the agent starts on Windows with no `vardir`/`conf-file` overrides
- **THEN** it reads `%ProgramData%\go-glpi-agent\agent.cfg` and writes state under `%ProgramData%\go-glpi-agent\var`

#### Scenario: Linux defaults unchanged

- **WHEN** the agent starts on Linux with no overrides
- **THEN** it still uses `/opt/go-glpi-agent/agent.cfg` and `/opt/go-glpi-agent/var`

### Requirement: Collectors SHALL be registered per platform

Each OS-specific collector package SHALL be blank-imported only in the binary for that OS,
gated by build tags, so a Windows binary never carries Linux collectors and vice versa.
Cross-platform `generic` collectors SHALL remain registered on every platform.

#### Scenario: Windows binary registry

- **WHEN** a Windows build runs an inventory cycle
- **THEN** only `windows/*` and `generic/*` collectors are eligible to run; no `linux/*` collector is registered

### Requirement: The module SHALL compile for FreeBSD

The Go module SHALL build successfully with `GOOS=freebsd GOARCH=amd64 go build ./...`,
and a FreeBSD binary SHALL register only the `freebsd/*` and cross-platform `generic/*`
collectors (no `linux/*` or `windows/*`). The existing Linux and Windows builds SHALL be
unaffected.

#### Scenario: Cross-compile for FreeBSD succeeds

- **WHEN** `CGO_ENABLED=0 GOOS=freebsd GOARCH=amd64 go build ./...` runs on the Linux CI host
- **THEN** the build completes without error and produces a FreeBSD binary

#### Scenario: FreeBSD binary registry

- **WHEN** a FreeBSD build runs an inventory cycle
- **THEN** only `freebsd/*` and `generic/*` collectors are eligible to run; no `linux/*` or `windows/*` collector is registered

#### Scenario: Linux and Windows builds unaffected

- **WHEN** `go build ./...` (Linux) and `GOOS=windows go build ./...` run
- **THEN** both still succeed and register only their own OS collectors plus `generic/*`
