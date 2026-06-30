## ADDED Requirements

### Requirement: The agent SHALL collect installed software from the registry

The agent SHALL enumerate installed software from the Windows uninstall registry keys —
`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`,
`HKLM\SOFTWARE\WOW6432Node\...\Uninstall` (32-bit on 64-bit Windows) and the per-user
`HKCU\...\Uninstall` — reporting name, version, publisher, install date and size. The agent
SHALL NOT use `Win32_Product` (it is slow and triggers MSI self-repair).

#### Scenario: Software list populated

- **WHEN** an inventory cycle runs on Windows
- **THEN** `SOFTWARES` contains entries with `NAME`, `VERSION` and `PUBLISHER`, sourced from the uninstall keys with `FROM` set to a registry source tag

#### Scenario: 32-bit and 64-bit entries both included

- **WHEN** the host is 64-bit and has both native and WOW6432Node uninstall entries
- **THEN** software from both hives is reported, de-duplicated by name+version

#### Scenario: System components hidden from Add/Remove are skipped

- **WHEN** an uninstall entry has `SystemComponent=1` or no `DisplayName`
- **THEN** that entry is skipped

### Requirement: The agent SHALL collect users and logged-in session on Windows

The agent SHALL report local users and groups (via WMI `Win32_UserAccount`/`Win32_Group`
scoped to the local machine) and the currently/last logged-in user
(`Win32_ComputerSystem.UserName`), populating `LASTLOGGEDUSER`.

#### Scenario: Local users reported

- **WHEN** the host has local accounts
- **THEN** `LOCAL_USERS` contains entries with login and a stable id (SID)

#### Scenario: Logged-in user reported

- **WHEN** a user is interactively logged in
- **THEN** `HARDWARE.LASTLOGGEDUSER` reflects that account
