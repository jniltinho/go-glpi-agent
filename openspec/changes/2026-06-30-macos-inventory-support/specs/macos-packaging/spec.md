## ADDED Requirements

### Requirement: The build SHALL produce dual-arch macOS binaries and installers

The `Makefile` and the macOS workflow SHALL build static `darwin/amd64` (Intel) and
`darwin/arm64` (Apple Silicon) binaries (`CGO_ENABLED=0`), and package each as a `.pkg`
(via `pkgbuild` + `productbuild`, with a `LaunchDaemon` for scheduled runs) wrapped in a
`.dmg` (via `hdiutil`). Artifacts SHALL be named `go-glpi-agent_<version>_x86_64.{pkg,dmg}`
and `go-glpi-agent_<version>_arm64.{pkg,dmg}`, matching the official agent's naming.

#### Scenario: Release produces the four macOS installers

- **WHEN** a `v*` tag is pushed and the release runs
- **THEN** the GitHub release includes `go-glpi-agent_<version>_x86_64.pkg`, `…_x86_64.dmg`, `…_arm64.pkg` and `…_arm64.dmg`, listed in `checksums.txt`

#### Scenario: Compile check in CI

- **WHEN** the `go.yml` CI workflow runs
- **THEN** it runs `GOOS=darwin GOARCH=amd64 go build ./...` and `GOOS=darwin GOARCH=arm64 go build ./...` and fails if either macOS build breaks

### Requirement: The agent SHALL run as a scheduled LaunchDaemon on macOS

The `.pkg` SHALL install the binary and config under `/usr/local/go-glpi-agent` and a
`LaunchDaemon` plist under `/Library/LaunchDaemons` that runs a periodic inventory (the
macOS analog of the systemd timer), loaded by a `postinstall` script, without clobbering
an existing `agent.cfg`. An `uninstall.sh` SHALL remove the daemon and files.

#### Scenario: Scheduled run configured

- **WHEN** an administrator installs the `.pkg`
- **THEN** the LaunchDaemon is loaded and the agent runs on a periodic schedule against the configured target

### Requirement: macOS inventory SHALL be validated on GitHub Actions against GLPI and the official agent

GitHub Actions SHALL be the validation environment. A `macos.yml` workflow SHALL run on a
matrix of `macos-13` (Intel/x86_64) and `macos-latest` (Apple Silicon/arm64). On each it
SHALL build the agent, run a real inventory, validate the native JSON against GLPI's
`inventory.schema.json`, install the official GLPI-Agent from its `.pkg` release matching
the runner architecture, run it, and print a per-section item-count comparison of the two
agents. The job SHALL fail if the Go agent's serial and UUID are both empty, and SHALL
upload the built `.pkg`/`.dmg` as artifacts.

#### Scenario: Native JSON schema-valid on both arches

- **WHEN** the macOS workflow runs `run` with `GFI_DUMP_JSON` set on each runner
- **THEN** the dumped JSON validates against GLPI's `inventory.schema.json` on both x86_64 and arm64

#### Scenario: Comparison against the official agent

- **WHEN** the workflow installs `GLPI-Agent-<ver>_<arch>.pkg` and runs it alongside the Go agent
- **THEN** the per-section item counts of both agents are printed side by side, and the core hardware sections (BIOS serial/UUID, CPU, memory, OS) are populated by both

#### Scenario: Serial never empty on CI runners

- **WHEN** the agent runs on a virtualized macOS runner whose serial may be redacted
- **THEN** the BIOS serial is non-empty (falling back to the UUID when needed), and the job asserts this

#### Scenario: Installers uploaded as artifacts

- **WHEN** the workflow finishes on each runner
- **THEN** the `go-glpi-agent_<version>_<arch>.pkg` and `…_<arch>.dmg` are available as downloadable build artifacts
