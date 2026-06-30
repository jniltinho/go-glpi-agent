# Contributing to go-glpi-agent

Thanks for contributing! This should get you from clone to a passing build in a
few minutes.

## Prerequisites

- Go 1.26+
- Optional, for packaging: [nfpm](https://nfpm.goreleaser.com) (`go install github.com/goreleaser/nfpm/v2/cmd/nfpm@latest`)
- Optional, for integration tests: Docker + Vagrant + VirtualBox (see `test/`)

## Build, test, run

```sh
git clone git@github.com:jniltinho/go-glpi-agent.git
cd go-glpi-agent

make build          # local binary ./go-glpi-agent
make test           # go test ./...
go vet ./...

./go-glpi-agent run --local /tmp/inv     # try it without a server
./go-glpi-agent version
```

## Conventions

- **English only** in Go code — comments, doc comments, log/error messages,
  identifiers. Every exported function/method and each package has a doc comment.
- **Cobra CLI**: `main.go` → `cmd.Execute()`; one subcommand per `cmd/<name>.go`;
  business logic stays in `internal/...`.
- **Module/repo**: `go-glpi-agent` (no host prefix). Binary installs under
  `/opt/go-glpi-agent`.
- Stdlib first, then the existing deps (`gopsutil`, `cobra`); don't add a
  dependency for a few lines of code.
- The inventory data model in `internal/inventory` is the single source of truth;
  the XML and JSON serializers both read from it.

See `AGENTS.md` for the full project conventions and the curated Go skills under
`.agents/skills/`.

## Pull requests

1. Branch from `main`.
2. Keep changes focused; add a test for non-trivial logic (`go test ./...` green).
3. Run `go vet ./...` and `gofmt -w .`.
4. Use clear commit messages (`feat:` / `fix:` / `docs:` / `ci:` / `refactor:`),
   which map to the `CHANGELOG.md` sections.
5. Update `CHANGELOG.md` under `[Unreleased]` when the change is user-visible.

## Releases

Releases are CHANGELOG-driven and automated by `.github/workflows/release.yml`
on a `v*` tag. See `.agents/skills/create-release/SKILL.md`.
