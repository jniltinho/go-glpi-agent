# Windows Vagrant acceptance lab

Validates the self-contained **Dotnet GLPI Agent** MSI on **Windows Server 2022**
against the project-local Docker GLPI stack. This lab is intended for a
dedicated test host with nested virtualization — not ordinary hosted CI runners.

## Host prerequisites

| Requirement | Notes |
| --- | --- |
| Disk | ≥ 40 GB free (Windows evaluation box ~10–15 GB) |
| RAM | ≥ 8 GB host; VM defaults to 4 GB |
| Virtualization | VirtualBox **or** Hyper-V (not both concurrently on Windows hosts) |
| Vagrant | 2.3+ with WinRM |
| Docker | For `../glpi` GLPI 10/11 lab |
| Windows MSI | Built on a Windows packager (`make package-windows`) |
| Licensing | Windows Server evaluation box terms apply |

Typical full run: **45–90 minutes** including box download on first use.

## Quick start

```sh
# 1. GLPI stack (from this repo tree)
../glpi/lab.sh start 10

# 2. On a Windows packager, build MSI + optional schema validator:
#    make package-windows VERSION=0.1.0
#    make publish-validator
# Copy artifacts/msi/*.msi into ./artifacts/

# 3. Orchestrate
./run-lab.sh prepare
./run-lab.sh up

# 4. Collect summary / destroy
./run-lab.sh collect
./run-lab.sh destroy
```

Environment overrides:

```sh
export GLPI_VERSION=11
export GLPI_SERVER=http://10.0.2.2:8181/front/inventory.php
export STAGES=install,local,schema,submit,compare,lifecycle,collect
export KEEP_RESOURCES=1   # leave install for debugging
export PROVIDER=virtualbox
./run-lab.sh up
```

## Stages

| Stage | What it does |
| --- | --- |
| `install` | Silent MSI install, ACL dump, service status |
| `local` | One-shot local JSON/XML inventory |
| `schema` | Validate JSON against container-extracted schema |
| `submit` | Native inventory to GLPI + soft API asset assert |
| `compare` | Optional Go / official Perl section comparison |
| `lifecycle` | Repair, uninstall preserve, reinstall identity (purge skipped by default) |
| `collect` | Bundle logs, Event Log sample, service state |

## Guest layout

```text
C:\dotnet-glpi-agent-test\
  package\agent.msi
  schema\inventory.schema.json
  scripts\...
  out\summary.json
  out\local\
  out\msi-lifecycle\
  logs\
```

## Network notes

- **VirtualBox NAT:** guest reaches host services at `10.0.2.2`. GLPI 10 lab port
  `8180`, GLPI 11 `8181`.
- **Hyper-V:** set `GLPI_SERVER` to a host address on the VM switch; verify with
  `Test-NetConnection` inside the guest before install.
- Preflight connectivity is recorded in `summary.json`.

## Optional reference agents

Place under `artifacts/ref/`:

- `go-glpi-agent.exe` — parent repo Windows build
- `GLPI-Agent-portable.zip` — official portable package

Then include the `compare` stage.

For upgrade/downgrade matrix, place a previous MSI at:

```text
artifacts/previous/dotnet-glpi-agent-previous-win-x64.msi
```

## Troubleshooting

| Symptom | Action |
| --- | --- |
| WinRM timeout | Increase `config.vm.boot_timeout`; ensure box supports WinRM |
| MSI missing | `./run-lab.sh prepare` after copying MSI into `artifacts/` |
| Schema fail | Re-run `../glpi/lab.sh schema 10` and re-provision files |
| Service stopped | Check `C:\ProgramData\DotnetGlpiAgent\logs` and Event Log |
| GLPI submit fail | Confirm Docker ports published; test `10.0.2.2:8180` from guest |
| Rerun provision only | `./run-lab.sh provision` with adjusted `STAGES` |
| Clean slate | `./run-lab.sh destroy` then `up` |

## Security

Lab credentials in `../glpi` are disposable. Do not expose containers beyond the
test host. Development MSIs are **unsigned**; production signing is a release gate.
