## ADDED Requirements

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
