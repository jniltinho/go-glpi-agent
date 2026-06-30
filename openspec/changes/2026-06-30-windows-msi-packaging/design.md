# Design — Windows MSI packaging

## Context

The agent is a single static `go-glpi-agent.exe`. The current Windows artifact is a
`.zip` + `install.ps1` (Scheduled Task), built on the Linux runner and validated on
`windows-latest`. This change adds an `.msi` with the same install behavior, built the
same way (on Linux), so managed estates can deploy via GPO/Intune/SCCM. No collector
or agent code changes.

## Goals / Non-goals

**Goals**
- An `.msi` that installs the binary + config, registers the hourly Scheduled Task,
  upgrades in place, and uninstalls cleanly by product code.
- Silent install with `SERVER`/`TAG` properties for unattended/GPO deployment.
- Preserve `agent.cfg` and `var` state across upgrades (no duplicate GLPI assets).
- Build on the **Linux** release runner (`wixl`); validate on `windows-latest`.

**Non-goals**
- Code-signing the MSI (needs a paid Authenticode cert + secrets) — follow-up.
- ADMX/GPO policy template; a Windows Service mode; arm64 Windows.

## Why MSI is easy here

A typical MSI is hard because of complex payloads (DLLs, runtimes, COM registration).
Ours has **one executable, one config file, and one scheduled task** — the minimal
case. The whole product is ~60 lines of WiX. The only non-trivial bits are the
scheduled task (no native WiX element) and config preservation, both solved below.

## Build toolchain

Two paths, same `.wxs`:

| Path | Tool | Runs on | Use |
|---|---|---|---|
| Primary | `wixl` (from **msitools**, `apt-get install wixl`) | **Linux** | CI release build — matches how the `.zip` is built today |
| Alt | `wix build` (WiX v5/v6, `dotnet tool install --global wix`) | Windows | local/dev builds on a Windows box |

`wixl` consumes WiX v3-schema `.wxs`, so the source targets that schema for portability.

## WiX product structure (`contrib/windows/msi/go-glpi-agent.wxs`)

- `<Product>` with a **fixed `UpgradeCode`** GUID (stable across versions — this is what
  makes upgrades work) and `Version="$(var.Version)"` passed in at build time.
- `<MajorUpgrade>` — `msiexec /i` of a newer version removes the old one first;
  `AllowSameVersionUpgrades` for reinstall.
- Install layout:
  - `ProgramFiles64Folder\go-glpi-agent\go-glpi-agent.exe` — one file component, keyed
    on the exe.
  - `CommonAppDataFolder\go-glpi-agent\agent.cfg` — config component with
    `NeverOverwrite="yes"` so upgrades keep operator edits; `CommonAppDataFolder\
    go-glpi-agent\var\` created empty for state.
- **Scheduled Task** (no native WiX element) via deferred `CustomAction`s wrapping
  `schtasks.exe`:
  - on install (`InstallExecuteSequence`, deferred/no-impersonate, after `InstallFiles`):
    `schtasks /Create /TN go-glpi-agent /SC HOURLY /RU SYSTEM /RL HIGHEST /TR
    "<INSTALLDIR>\go-glpi-agent.exe run" /F`;
  - on uninstall: `schtasks /Delete /TN go-glpi-agent /F` (ignore "not found").
  - Rollback CAs delete the task if install fails.
- **Config seeding with SERVER/TAG**: a deferred CA runs the installed exe / a tiny
  inline script that, **only when `agent.cfg` did not already exist**, writes
  `server = [SERVER]` and `tag = [TAG]` (when provided). Property values reach the
  deferred CA via `CustomActionData`. Default `SERVER`/`TAG` empty → the shipped
  template `agent.cfg` is used verbatim (operator edits it, like the `.zip` flow).
- **Uninstall/PURGE**: binary + task always removed; `agent.cfg`/`var` kept unless
  `PURGE=1` is passed (a CA removes the data dir in that case), mirroring
  `uninstall.ps1`'s default-preserve behavior.

## CLI / deployment surface

```
# interactive
msiexec /i go-glpi-agent_<ver>_x64.msi

# silent, unattended (GPO/Intune/SCCM)
msiexec /i go-glpi-agent_<ver>_x64.msi /qn SERVER=http://glpi/front/inventory.php TAG=dc1

# upgrade: just install the newer MSI (same UpgradeCode) — old version removed first
# uninstall (keep config):  msiexec /x go-glpi-agent_<ver>_x64.msi /qn
# uninstall (purge config):  msiexec /x go-glpi-agent_<ver>_x64.msi /qn PURGE=1
```

## CI validation (windows-latest)

A `windows-latest` job (extending the existing `windows-smoke` or a sibling) does the
round-trip on real Windows:

1. download/build the `.msi` (CI can `wixl`-build on Linux in a prior job and pass it
   as an artifact, or build with `wix` on the Windows runner);
2. `msiexec /i go-glpi-agent.msi /qn SERVER=... /l*v install.log` → assert exit 0;
3. assert `schtasks /Query /TN go-glpi-agent` succeeds and the exe is under
   `%ProgramFiles%`;
4. run an inventory with `GFI_DUMP_JSON`; validate against GLPI's
   `inventory.schema.json` (reuse the existing schema step);
5. `msiexec /x go-glpi-agent.msi /qn` → assert the task and binary are gone;
6. upload the `.msi` artifact.

## Risks / tradeoffs

- **Unsigned MSI** → SmartScreen/UAC warns on manual download; silent GPO/Intune push
  is unaffected. Signing is a documented follow-up needing a cert.
- **GUID management** → the `UpgradeCode` must never change; component GUIDs are stable
  per install path. Documented in the `.wxs` header.
- **`wixl` feature parity** → `wixl` covers the v3 elements we use (Directory,
  Component, File, CustomAction, MajorUpgrade). If a needed element is missing, the
  fallback is building with `wix`/WiX on the Windows runner — same `.wxs` kept v3-clean.
- **Scheduled task via schtasks CA** → less declarative than a native element but
  identical to today's `install.ps1` and trivially testable (`schtasks /Query`).
