## 1. WiX source

- [ ] 1.1 Add `contrib/windows/msi/go-glpi-agent.wxs` (WiX v3 schema, `wixl`-compatible): `<Product>` with a fixed `UpgradeCode`, `Version=$(var.Version)`, `<MajorUpgrade AllowSameVersionUpgrades="yes">`, and the `Platform="x64"` package.
- [ ] 1.2 Install layout: `ProgramFiles64Folder\go-glpi-agent\go-glpi-agent.exe` (file component); `CommonAppDataFolder\go-glpi-agent\agent.cfg` with `NeverOverwrite="yes"`; create `…\var\`.
- [ ] 1.3 Scheduled Task via deferred `CustomAction`s wrapping `schtasks.exe`: create on install (`/SC HOURLY /RU SYSTEM /RL HIGHEST /TR "<INSTALLDIR>\go-glpi-agent.exe run" /F`), delete on uninstall (`/F`, ignore not-found); add rollback CA.
- [ ] 1.4 Public properties `SERVER`/`TAG`: deferred CA writes them into `agent.cfg` only when the config did not pre-exist (values via `CustomActionData`); empty → ship the template `agent.cfg` verbatim.
- [ ] 1.5 Uninstall: always remove binary + task; keep `agent.cfg`/`var` unless `PURGE=1` (CA removes the data dir), mirroring `uninstall.ps1`.

## 2. Build target

- [ ] 2.1 Add `make package-msi`: build `go-glpi-agent.exe` (`GOOS=windows GOARCH=amd64`), then `wixl -D Version=$(VERSION) -o dist/go-glpi-agent_$(VERSION)_x64.msi contrib/windows/msi/go-glpi-agent.wxs` (+ stage the exe/agent.cfg payload).
- [ ] 2.2 Document the alternative toolchains in `contrib/windows/msi/README.md`: `go-msi` (`wix.json`, WiX-under-Wine container) and WiX-via-Wine images (`dactylos/wix`, `electronuserland/builder`) as Linux fallbacks if `wixl` lacks a needed feature, plus `wix build` (WiX v5/v6) on a Windows host. Keep the `.wxs` v3-clean so every path consumes it unchanged.
- [ ] 2.3 Verify `wixl` is apt-installable on the Ubuntu runner (`apt-get install -y wixl`); the MSI builds on Linux with no Windows tooling.

## 3. CI validation (windows-latest round-trip)

- [ ] 3.1 Build the `.msi` (on the Linux job via `wixl`, uploaded as an artifact, or with `wix` on the Windows runner).
- [ ] 3.2 On `windows-latest`: `msiexec /i go-glpi-agent.msi /qn SERVER=<glpi> /l*v install.log` → assert exit 0; dump the log on failure.
- [ ] 3.3 Assert the install: `schtasks /Query /TN go-glpi-agent` succeeds and `%ProgramFiles%\go-glpi-agent\go-glpi-agent.exe` exists; `agent.cfg` under `%ProgramData%` has the injected `server`.
- [ ] 3.4 Run an inventory with `GFI_DUMP_JSON`; validate the native JSON against GLPI's `inventory.schema.json` (reuse the existing schema step).
- [ ] 3.5 `msiexec /x go-glpi-agent.msi /qn` → assert the Scheduled Task and the binary are gone (config-preserve path); add a second case asserting `PURGE=1` removes the data dir.
- [ ] 3.6 `actions/upload-artifact` the `.msi`.

## 4. Release & docs

- [ ] 4.1 Extend `release.yml` to build the `.msi` on the Linux runner (`apt-get install wixl` + `make package-msi`) and publish `go-glpi-agent_<version>_x64.msi` (added to `checksums.txt` + the release) alongside the `.zip`.
- [ ] 4.2 Update README/`docs/REFERENCE.md`: MSI install (`msiexec /i … /qn SERVER=…`), the GPO/Intune/SCCM deployment note, upgrade and uninstall (incl. `PURGE=1`); note the MSI is unsigned (signing is a follow-up).
- [ ] 4.3 Update `CHANGELOG.md`; confirm `make package-msi` produces a valid MSI (`msiinfo`/`msiexec` round-trip green in CI).
