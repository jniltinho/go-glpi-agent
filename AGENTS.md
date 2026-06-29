# AGENTS.md

Guidance for AI agents (and humans) working on **go-fusioninventory-agent**.

## What this is

A Go reimplementation of the FusionInventory/GLPI inventory agent for **Linux**.
It produces a single static binary (`fusioninventory-agent`) that collects local
hardware/software inventory and sends it to a **GLPI 10+** server using the
native JSON protocol, with automatic fallback to the legacy OCS/FusionInventory
XML protocol. It can also write the inventory to a local XML file.

The Go agent lives at the repository root. The Perl reference projects (kept
intact, for behavior comparison only) live under `base/` (`base/perl/`,
`base/glpi-agent/`).

## Conventions (follow these)

- **English only.** All Go code — comments, doc comments, log/error messages,
  identifiers — is written in English. Do not introduce Portuguese into the code.
- **Cobra CLI layout**, matching the other Go projects in this account
  (e.g. `go-manager-server`, `go-rundeck`):
  - `main.go` at the root is a thin entrypoint that calls `cmd.Execute()`.
  - `cmd/root.go` defines the root command and shared/persistent flags.
  - `cmd/<name>.go` defines one subcommand each (`run`, `daemon`, `version`).
  - Business logic stays in `internal/...`, never in `cmd/`.
- **Module path:** `go-fusioninventory-agent` (no host prefix). The GitHub
  remote is `github.com/jniltinho/go-fusioninventory-agent`.
- **Stdlib first, then existing deps.** The only runtime dependencies are
  `gopsutil` (collection) and `cobra` (CLI); inventory IDs use `crypto/rand`
  (no UUID dependency). Don't add a dependency for what a few lines can do.
- Keep the data model in `internal/inventory` as the single source of truth;
  the XML and JSON serializers both read from it (see `internal/transport/server`).

## Go skills

This repo ships curated Go skills under `.agents/skills/`. Consult them when
relevant instead of guessing conventions:

- `golang-spf13-cobra`, `golang-spf13-viper` — CLI/config patterns
- `golang-code-style`, `golang-naming`, `golang-documentation` — style
- `golang-error-handling`, `golang-structs-interfaces`, `golang-concurrency`
- `golang-testing`, `golang-lint`, `golang-modernize`, `golang-security`

## Planning workflow

Specs and tasks live in `openspec/changes/`. Use the OpenSpec skills
(`openspec-apply-change`, `openspec-propose`, etc.) to drive implementation; keep
`tasks.md` checkboxes up to date as work lands.

## Build, test, run

```sh
make build          # local binary ./fusioninventory-agent
make build-all      # static linux/amd64 in dist/
make test           # go test ./...
go vet ./...

./fusioninventory-agent run --local /tmp/inv          # write XML locally
./fusioninventory-agent run --server http://glpi/front/inventory.php
./fusioninventory-agent daemon                         # periodic cycles
./fusioninventory-agent version
```

The agent reads the same `agent.cfg` (INI) as the Perl agent; CLI flags override
the file.

## Test infrastructure

`test/` holds integration infra to run **on a test host**, not the dev machine:

- `test/glpi/` — GLPI 10 + MariaDB via `docker compose`. Enable native
  inventory before testing: set `glpi_configs.enabled_inventory = 1`
  (context `inventory`) and clear the GLPI cache (`php bin/console cache:clear`).
- `test/vagrant/` — Rocky 9 and Debian 12 VMs (VirtualBox/libvirt); run
  `make build-all` first so `dist/fusioninventory-agent` is mounted into the VMs.

When validating the native protocol, GLPI's strict `inventory.schema.json`
(in the GLPI container under `vendor/glpi-project/inventory_format/`) is the
source of truth for field types/enums — the JSON serializer normalizes dates,
arch, and a few typed fields to satisfy it.
