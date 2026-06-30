## Why

The agent already ships a Windows distribution — a `.zip` with `install.ps1` that
registers an hourly Scheduled Task. That works for hands-on installs, but fleets
deploy software through **MSI**: Group Policy (GPO) software installation, Microsoft
Intune, SCCM/ConfigMgr and PDQ all expect an `.msi` they can push silently, upgrade
in place and uninstall by product code. The official glpi-agent ships an `.msi` for
exactly this reason. Adding one makes go-glpi-agent a drop-in for managed Windows
estates without changing any collector code.

Because the agent is a **single static `.exe`** (no Perl runtime, no DLLs), the MSI
is small and simple: one file component, one config component, and a scheduled task.
It can be authored with WiX and — importantly — built with **`wixl` (msitools) on the
existing Linux release runner**, matching how the Windows `.zip` is already produced
on Linux today (no Windows-only build tooling required). Validation still happens on
a real `windows-latest` runner via `msiexec`.

## What Changes

- Add a WiX source (`contrib/windows/msi/go-glpi-agent.wxs`) describing the product:
  - the `.exe` installed under `%ProgramFiles%\go-glpi-agent`;
  - `agent.cfg` seeded under `%ProgramData%\go-glpi-agent` with **NeverOverwrite** so
    upgrades keep the operator's config and the `var` state (deviceid/agentid) — the
    host is not re-created as a new GLPI asset;
  - a **Scheduled Task** (`go-glpi-agent`, hourly, as SYSTEM) created on install and
    removed on uninstall — the same behavior as `install.ps1` today;
  - a fixed **UpgradeCode** + `MajorUpgrade` so `msiexec /i` over an older version
    upgrades cleanly and `msiexec /x` removes everything (task, binary) while
    preserving config unless a `PURGE=1` property is passed;
  - public properties **SERVER** and **TAG** so `msiexec /i go-glpi-agent.msi /qn
    SERVER=http://glpi/front/inventory.php TAG=dc1` writes them into `agent.cfg` on a
    fresh install (skipped when a config already exists).
- Add a `make package-msi` target that builds the `.exe` and runs `wixl` to emit
  `go-glpi-agent_<version>_x64.msi` on Linux (with a documented WiX/`wix build` path
  for building on Windows).
- Extend CI: the existing `windows-latest` job (or a sibling) installs the MSI
  silently with `msiexec /qn`, asserts the Scheduled Task exists, runs an inventory,
  validates the native JSON against GLPI's `inventory.schema.json`, then uninstalls
  with `msiexec /x /qn` and asserts the task and binary are gone. `release.yml`
  publishes the `.msi` (built on Linux) alongside the existing `.zip`.

## Capabilities

### New Capabilities
- `windows-msi-packaging`: build a Windows `.msi` (WiX/`wixl`) that installs the
  binary + config, registers the hourly Scheduled Task, supports silent install with
  `SERVER`/`TAG` properties, preserves config across upgrades, uninstalls cleanly by
  product code, and is built on the Linux runner and validated end-to-end on
  `windows-latest`.

## Impact

- **Code**: none — no collector or agent change; the `.exe` is unchanged.
- **Dependencies**: build-time only — `wixl` (`msitools`, apt-installable) on the
  Linux runner; no new Go dependency.
- **Build/CI**: `Makefile` gains `package-msi`; `go.yml`/a Windows job adds the
  `msiexec` install→verify→uninstall round-trip; `release.yml` publishes the `.msi`.
- **Packaging**: `contrib/windows/msi/` (the `.wxs` and any helper script).
- **Docs**: README/REFERENCE Windows section gains the MSI/`msiexec` install path and
  the GPO/Intune deployment note.
- **Out of scope**: Authenticode code-signing of the `.msi` (needs a paid cert +
  secrets — documented follow-up); an ADMX/GPO policy template; a Windows **Service**
  install mode (the Scheduled Task stays the default; a service is a possible
  follow-up); arm64 Windows.
