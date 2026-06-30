# go-glpi-agent Windows MSI

An `.msi` for deploying go-glpi-agent through **GPO software installation, Intune,
SCCM/ConfigMgr or PDQ** — the formats managed Windows estates expect. The agent is
a single static `.exe`, so the MSI is the minimal case: one file + a Scheduled Task,
built **on Linux** (no Windows host) with `wixl`.

## What it does

Installing the `.msi`:

- copies `go-glpi-agent.exe` to `C:\Program Files\go-glpi-agent\`;
- writes the `SERVER`/`TAG` install properties to `HKLM\Software\go-glpi-agent`;
- runs the bundled exe's hidden `service` subcommands (deferred, as SYSTEM) to:
  - seed `C:\ProgramData\go-glpi-agent\agent.cfg` (only if absent — upgrade-safe),
  - append `server`/`tag` from the install properties,
  - register the hourly `go-glpi-agent` Scheduled Task (SYSTEM, the analog of the
    systemd timer).

A stable `UpgradeCode` + `<Upgrade>` give in-place upgrades; uninstall removes the
task and binary; `PURGE=1` also deletes the config/state.

## Install / upgrade / uninstall

```bat
:: interactive
msiexec /i go-glpi-agent_<ver>_x64.msi

:: silent, unattended (GPO / Intune / SCCM)
msiexec /i go-glpi-agent_<ver>_x64.msi /qn SERVER=http://glpi/front/inventory.php TAG=dc1

:: upgrade: just install the newer MSI (same UpgradeCode) — old version removed first
:: uninstall (keep config):  msiexec /x go-glpi-agent_<ver>_x64.msi /qn
:: uninstall (purge config):  msiexec /x go-glpi-agent_<ver>_x64.msi /qn PURGE=1
```

> The `.msi` is **unsigned**. SmartScreen/UAC may warn on a manual download; silent
> GPO/Intune deployment is unaffected. Authenticode signing is a follow-up (needs a
> certificate).

## Building the MSI

The same `go-glpi-agent.wxs` (WiX v3 schema) builds with any of these. The repo uses
**`wixl`** — native Linux, no Wine.

### 1. wixl (msitools) — primary, native Linux

```sh
sudo apt-get install -y wixl msitools
make package-msi VERSION=1.2.3        # → dist/go-glpi-agent_1.2.3_x64.msi
```

### 2. Docker — reproducible, no local toolchain

```sh
docker build -f contrib/windows/msi/Dockerfile -t go-glpi-agent-msi .
docker run --rm -e VERSION=1.2.3 -v "$PWD/dist:/out" go-glpi-agent-msi
```

### 3. go-msi or WiX-via-Wine — alternatives for richer WiX features

If a future need outgrows `wixl`'s element subset (it supports neither
`Permanent`/`NeverOverwrite` nor `Package/@Platform`, which is why the agent owns its
config and the arch comes from `wixl -a x64`):

- **[`go-msi`](https://github.com/mh-cbon/go-msi)** — a Go-friendly wrapper that
  builds an MSI from a `wix.json`, running real WiX inside a Wine container.
- **WiX via Wine** — images like `dactylos/wix` or `electronuserland/builder` run the
  full WiX 3/4 toolset under Wine on Linux.

### 4. WiX on Windows — for local Windows dev

```powershell
dotnet tool install --global wix
wix build contrib\windows\msi\go-glpi-agent.wxs -d SourceDir=dist\msi -d Version=1.2.3 `
    -arch x64 -o dist\go-glpi-agent_1.2.3_x64.msi
```

## Implementation notes (wixl quirks)

`wixl` 0.103 ignores a few WiX v3 attributes, so the design works around them:

| Not supported by wixl | Worked around by |
|---|---|
| `Package/@Platform` | `wixl -a x64` |
| `Component/@Permanent`, `@NeverOverwrite` | the exe owns `agent.cfg` (writes a default only when absent) |
| `CustomAction/@Directory` | custom actions use `FileKey` (run the installed exe) |
| `Before`/`After` for custom actions | explicit `Sequence` numbers in `InstallExecuteSequence` |

The custom actions therefore run the bundled exe (`service install|configure|
uninstall|purge`), which self-locates with `os.Executable()` — nothing needs the
install path to survive into the deferred (SYSTEM) phase.
