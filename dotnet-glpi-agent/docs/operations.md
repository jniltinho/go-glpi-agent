# Operations guide

## Supported matrix (v1)

| Layer | Versions |
| --- | --- |
| Windows | 10/11 x64, Server 2022 (required baseline), Server 2025 x64 |
| GLPI | 10 and 11 native inventory |
| Auth | none, HTTP basic, proxy, TLS client certificate |
| OAuth2 | deferred after v1 |

## CLI

```text
dotnet-glpi-agent version
dotnet-glpi-agent validate-config --config C:\Program Files\DotnetGlpiAgent\agent.cfg
dotnet-glpi-agent run --local C:\temp\inv
dotnet-glpi-agent run --server https://glpi.example/front/inventory.php --force
```

Configuration precedence: CLI → `GLPI_AGENT_*` environment → `agent.cfg` → defaults.

Common keys: `server`, `local`, `tag`, `delaytime`, `lazy`, `force`,
`backend-collect-timeout`, `no-category`, `scan-processes`, `scan-profiles`,
proxy/TLS settings, `logger`, `logfile`, `vardir`, compression.

## Windows Service

Service name: `DotnetGlpiAgent`. Installed by the MSI as LocalSystem with delayed
automatic start and SCM restart on failure. See `windows-service.md`.

```powershell
Get-Service DotnetGlpiAgent
Restart-Service DotnetGlpiAgent
```

## Silent MSI deployment

```powershell
msiexec /i dotnet-glpi-agent-0.1.0-win-x64.msi /qn /L*v install.log `
  SERVER="https://glpi.example/front/inventory.php" `
  TAG="site-a" STARTSERVICE=1 RUNNOW=0
```

Secrets:

```powershell
& "C:\Program Files\DotnetGlpiAgent\Set-AgentSecret.ps1" -Name password
```

Upgrade: install the newer MSI (major upgrade).  
Uninstall: preserves ProgramData.  
Purge: `msiexec /x {ProductCode} /qn PURGE=1` (destructive).

Details: `msi-packaging.md`, `packaging/wix/README.md`.

## Test laboratory

```sh
# GLPI containers
./test/glpi/lab.sh start 10

# Windows VM (dedicated host)
./test/vagrant-windows/run-lab.sh prepare
./test/vagrant-windows/run-lab.sh up
./test/vagrant-windows/run-lab.sh destroy
```

## Logging

- Rolling files: `%ProgramData%\DotnetGlpiAgent\logs`
- Service lifecycle: Windows Application Event Log
- Secrets are redacted from structured diagnostics
