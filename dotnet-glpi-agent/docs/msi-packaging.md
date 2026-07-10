# MSI packaging and deployment

See also `packaging/wix/README.md` for build commands and silent properties.

## Why ordinary MSI secrets are rejected

Windows Installer records public property values in:

- the verbose MSI log (`/L*v`)
- the in-progress installation database
- sometimes process command lines visible to other administrators

Fleet passwords, proxy credentials, and client-certificate passwords therefore
must never be supplied as `msiexec` properties. Use the elevated
`Set-AgentSecret.ps1` script shipped under Program Files, which:

1. requires an administrator token
2. accepts an interactive secure prompt or a tightly ACLed secret file
3. rewrites `agent.cfg` atomically
4. hardens ACLs to SYSTEM + Administrators only
5. deletes the secret file when one was supplied
6. restarts the service when present

## First-install configuration

`agent.cfg` is seeded only when missing (`Permanent` + `NeverOverwrite`). Silent
`SERVER` / `TAG` values are appended through the MSI `IniFile` table under the
`[agent]` section. The configuration loader accepts both flat and sectioned
keys.

Default `local` output under ProgramData lets the Windows Service start even
when no GLPI server is configured yet.

## Upgrade and identity

The package family `UpgradeCode` is stable. Product version comes from
`ProductVersion` / `VersionPrefix`. Major upgrades schedule after
`InstallInitialize`, block downgrades, and leave ProgramData intact so device
and agent identifiers survive.

## Release artifacts

`packaging/scripts/New-ReleaseArtifacts.sh` (or `.ps1` on Windows) produces:

- versioned MSI name
- SHA-256 checksums
- dependency SBOM snapshot (`dotnet list package`)
- third-party notices copy
- unsigned development labeling when signatures are absent
