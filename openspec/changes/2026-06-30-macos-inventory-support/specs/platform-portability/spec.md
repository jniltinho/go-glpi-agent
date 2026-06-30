## ADDED Requirements

### Requirement: The module SHALL compile for macOS on both architectures

The Go module SHALL build successfully with `GOOS=darwin GOARCH=amd64 go build ./...`
and `GOOS=darwin GOARCH=arm64 go build ./...`, and a macOS binary SHALL register only the
`macos/*` and cross-platform `generic/*` collectors (no `linux/*`, `windows/*` or
`freebsd/*`). The existing Linux, Windows and FreeBSD builds SHALL be unaffected.

#### Scenario: Cross-compile for macOS succeeds on both arches

- **WHEN** `CGO_ENABLED=0 GOOS=darwin GOARCH=amd64 go build ./...` and `…GOARCH=arm64 go build ./...` run on the Linux CI host
- **THEN** both complete without error and produce macOS binaries

#### Scenario: macOS binary registry

- **WHEN** a macOS build runs an inventory cycle
- **THEN** only `macos/*` and `generic/*` collectors are eligible to run; no `linux/*`, `windows/*` or `freebsd/*` collector is registered

#### Scenario: Other OS builds unaffected

- **WHEN** `go build ./...` (Linux), `GOOS=windows go build ./...` and `GOOS=freebsd go build ./...` run
- **THEN** all still succeed and register only their own OS collectors plus `generic/*`
