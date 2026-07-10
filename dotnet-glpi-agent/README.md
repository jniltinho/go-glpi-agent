# Dotnet GLPI Agent

Windows-first GLPI inventory agent built with **.NET 10 LTS**. The project
combines a typed, testable architecture with broad Windows inventory coverage
and is kept independent from the Go and Perl implementations in the parent
repository.

## Layout

| Path | Purpose |
| --- | --- |
| `src/DotnetGlpiAgent.Core` | Protocol-neutral model, config, identity, orchestration |
| `src/DotnetGlpiAgent.Windows` | WMI/registry/BCL adapters and collectors |
| `src/DotnetGlpiAgent.Protocol` | Native JSON + legacy XML transports |
| `src/DotnetGlpiAgent.App` | CLI + Windows Service composition root |
| `tests` | Unit, contract, fixture, integration tests |
| `packaging/wix` | WiX 4.0.6 MSI authoring |
| `packaging/scripts` | Secret provisioning, release artifacts, MSI lifecycle |
| `test/glpi` | Docker GLPI 10/11 laboratory |
| `test/vagrant-windows` | Windows Server 2022 acceptance VM |
| `docs` | Product decisions, coverage, service, MSI, operations |

## Build and test

SDK **10.0.301** is pinned by `global.json`.

```sh
make restore build test
# or:
dotnet restore DotnetGlpiAgent.sln
dotnet build DotnetGlpiAgent.sln -c Release --no-restore
dotnet test DotnetGlpiAgent.sln -c Release --no-build
```

Self-contained publish (no machine-wide .NET runtime required):

```sh
make publish
# artifacts/publish/win-x64/dotnet-glpi-agent.exe
```

Trimming and Native AOT stay disabled for WMI/COM compatibility.

## MSI package (Windows packager only)

```sh
make package-windows VERSION=0.1.0
make release-artifacts VERSION=0.1.0
```

WiX Toolset only builds MSI on Windows. Development packages are labeled
`.UNSIGNED.txt`. See `docs/msi-packaging.md` and `packaging/wix/README.md`.

## Run

```text
dotnet-glpi-agent version
dotnet-glpi-agent validate-config --config agent.cfg
dotnet-glpi-agent run --local C:\temp\inv
dotnet-glpi-agent run --server https://glpi.example/front/inventory.php --force
```

Service mode is selected by the Windows Service command line (`service --config ...`).

## Test laboratory

```sh
./test/glpi/lab.sh start 10
./test/vagrant-windows/run-lab.sh prepare   # after copying MSI into artifacts/
./test/vagrant-windows/run-lab.sh up        # dedicated host with VirtualBox/Hyper-V
```

## Documentation

- `docs/product-decisions.md` — naming, WiX legal gate, signing ownership
- `docs/collector-coverage.md` — inventory category matrix
- `docs/windows-service.md` — service account and scheduling
- `docs/msi-packaging.md` — deploy, secrets, upgrades, purge
- `docs/operations.md` — CLI, service, silent install, lab
- `docs/clean-room.md` — GPL reference boundaries

## OpenSpec

Implementation is tracked by change `add-dotnet10-windows-agent`.
