# Inventory parity with the official Perl GLPI Agent

## Scope

The .NET agent targets **inventory** feature parity with
`base/glpi-agent` (Windows modules under
`Task/Inventory/Win32/`). It does **not** implement non-inventory tasks
that the full Perl agent ships (deploy, network discovery, ESX, Wake-on-LAN,
remote inventory, collect). Those remain explicit non-goals of the OpenSpec
design (D non-goals).

Related docs (read before claiming parity):

| Doc | Role |
| --- | --- |
| `docs/collector-coverage.md` | Per-category Windows sources vs Perl/Go |
| `docs/claude-validation-report.md` | 2026-07-10 pass-with-notes + P0/P1/P2 backlog |
| `docs/product-decisions.md` | OAuth2 deferred, WiX 4.0.6, signing owner |
| `docs/operations.md` | CLI, service, MSI fleet ops |
| `docs/identity-migration.md` | Go → Dotnet identity adoption |
| `openspec/changes/add-dotnet10-windows-agent/` | Specs + design non-goals |

## Validated matrix (Win2022 eval VM, 2026-07-10)

| Section | Dotnet | Perl 1.18 | Go | Notes |
| --- | ---: | ---: | ---: | --- |
| OPERATINGSYSTEM | 1 | 1 | 1 | OK |
| HARDWARE / BIOS / CPUS | 1 | 1 | 1 | OK |
| MEMORIES | 1+ | 0* | 0* | Synthetic total when SMBIOS slots empty |
| DRIVES | 1 | 3 | 1 | Perl counts more ready/volume shapes |
| STORAGES | 1 | 1 | 1 | OK |
| NETWORKS | 9 | 3 | 5 | Dotnet richer (per-address) |
| SOFTWARES | 39 | 41 | 3 | Within threshold (≤10 of Perl) |
| CONTROLLERS | 5+ | 8 | 0 | USB/SCSI/IDE; Perl may count more PnP |
| FIREWALL | 3 | 3 | 0 | WMI + registry fallback |
| VIDEOS | 1 | 1 | 0 | Remote-display adapters filtered |
| PRINTERS / ANTIVIRUS | ≥1 | ≥1 | 0 | OK |
| LOCAL_GROUPS | 25 | 25 | 25 | OK |
| LOCAL_USERS | 5 | 2 | 5 | OK |
| MONITORS / USB / BATTERIES | 0 | 0 | 0 | Absent on this Server VM |
| SOUNDS | 0 | varies | 0 | Often empty on Server Core |

\* On this VirtualBox image `Win32_PhysicalMemory` may be empty for all agents;
the Dotnet agent now emits a synthetic system-total module.

## Module status (Perl Win32 → Dotnet)

Cross-checked 2026-07-10 against all 27 Perl `Task/Inventory/Win32/*.pm` modules
and `dotnet-glpi-agent/src/DotnetGlpiAgent.Windows/Collectors/` (Claude review +
local docs). Schema note: native JSON uses `firewalls` (not XML `FIREWALL`).

### Missing sections (schema-valid, worth implementing)

| Perl | Status | Gap | Effort |
| --- | --- | --- | --- |
| `Environment.pm` | **Missing** | `ENVS` (`Win32_Environment` / machine env) | S |
| `Slots.pm` | **Missing** | `SLOTS` (`Win32_SystemSlot`) | S |
| `License.pm` | **Missing** | `LICENSEINFOS` (SLP WMI + Office keys + DigitalProductId) | M |
| `Registry.pm` | **Missing** | Server-requested `REGISTRY` (PROLOG-driven) | Defer (legacy-only) |
| `Storages/HP.pm` | **Missing** | `hpacucli` RAID CLI | Non-goal (vendor CLI) |

### Partial (concrete field/source gaps)

| Area | Dotnet | Priority gaps |
| --- | --- | --- |
| Drives | `VolumeCollector` | Volume serial, BitLocker ENCRYPT_*, `Win32_Volume` letterless mounts |
| Softwares | `SoftwareCollector` | Real MSI uninstall GUID (not synthetic), HELPLINK, LastWrite install date |
| Hardware | `HardwareCollector` | WINPRODKEY/WINPRODID/WINLANG/WINCOMPANY/WINOWNER, DESCRIPTION, LASTLOGGEDUSER |
| Chassis | (in Hardware) | Expand ~5 chassis names → Perl’s ~25 DMI table |
| Videos | `PeripheralCollector` | `qwMemorySize` for GPUs >4 GB (AdapterRAM is uint32) |
| Controllers | `StorageCollector` | Full PCI VEN/DEV/SUBSYS parse; broader PnP classes |
| Networks | `NetworkCollector` + mapper | Emit `ipdhcp`/`dns`; HARDWARE default_gateway/dns aggregation |
| OS | mapper | Emit SERVICE_PACK (Build.UBR), timezone |
| CPU | mapper / registry | FAMILYNUMBER/MODEL/STEPPING; socket designation emission |
| AntiVirus | collectors | Defender BASE_VERSION; **no** vendor-CLI probes (non-goal) |
| Processes | `ProcessCollector` | `virtualmemory` mislabel (WorkingSet) |
| Storages | `StorageCollector` | Prefer `MSFT_PhysicalDisk`; add CD-ROM/tape |
| Monitors | `MonitorCollector` | Emit resolution + raw EDID BASE64 |
| Batteries | `BatteryCollector` | `BatteryStaticData` fallback; no `powercfg` shell-out |
| Users | `UserSessionCollector` | LASTLOGGEDUSER; Azure AD UPN rewrite |

### Present / adequate

Bios (minor SMBIOS version drop), Memory, Modems, Sounds, Inputs, Ports,
Firewall, USB (usb.ids naming deferred — GLPI can resolve server-side).

## Protocol / config gaps (inventory path)

| Item | Status | Action |
| --- | --- | --- |
| CONTACT `name`/`version` | Wrong product name; version omitted | Fix `NativeJsonSerializer` |
| CONTACT `status:error` | Not checked before inventory | Fix `GlpiClient` |
| CONTACT expiration / disabled / server categories | Parsed or ignored incompletely | Wire to scheduler + config |
| Pending poll delay | Fixed 1s vs server expiration | Align with server delay |
| Legacy PROLOG `RESPONSE`/`PROLOG_FREQ` | Not parsed | Implement for real OCS fallback |
| OAuth2 | Deferred (`product-decisions.md`) | Amend protocol spec to match |
| Multi-value `server`/`local`/`no-category` | Single value only | Fleet migration blocker |
| `proxy = none` | Throws | Accept Perl’s disable token |
| `required-category` | Missing | Add with CONTACT answer honor |
| `ssl-fingerprint` | Missing | Common self-signed fleets |

## Evidence / lab gaps

From `claude-validation-report.md` (still open after v0.5.0 ship):

1. Regenerate E2E evidence with the committed `run-lab.sh` pipeline (UTF-8).
2. Make `Assert-GlpiAsset.ps1` assert real GLPI API fields (not tautological).
3. Encode `Compare-Inventories.ps1` thresholds and fail CI/lab on regression.
4. MSI repair/remember-property + deterministic harvest GUIDs: one Windows guest proof.
5. Lab hardware blind spots (no battery/monitor/large GPU/BitLocker/Entra) — add Win11 client or fixture assertions.

## Top 10 next actions (impact order)

| # | Action | Effort | Risk |
| ---: | --- | --- | --- |
| 1 | CONTACT payload (`name`/`version`) + `status:error` gate | S | Low |
| 2 | Mapper-only fields: `ipdhcp`/`dns`, OS service_pack/timezone, monitor resolution | S | Low |
| 3 | `EnvironmentCollector` + `SlotCollector` | S | Low |
| 4 | Softwares: real MSI GUID + HELPLINK + LastWrite date | S | Low* |
| 5 | Drives: serial + `Win32_Volume` + BitLocker | M | Med |
| 6 | LICENSEINFOS + HARDWARE WINPROD* (shared DigitalProductId) | M | Med |
| 7 | Honor CONTACT answer (expiration, disabled, categories) + `required-category` | M | Med |
| 8 | Storages: `MSFT_PhysicalDisk` + CD-ROM; Videos: `qwMemorySize` | M | Low |
| 9 | Controllers: full PCI PNPDeviceID parse (reuse dead `PnpDevices`) | M | Low |
| 10 | Lab: thresholds, real Assert-GlpiAsset, regenerate E2E evidence | M | Low |

\* MSI GUID change may re-key existing GLPI software links once across agents.

## Intentional non-goals (re-document, do not “fix”)

- Non-inventory tasks: deploy, netdiscovery, ESX, Wake-on-LAN, remote inventory, collect.
- OAuth2 client credentials until an explicit product decision lifts the deferral.
- Vendor CLI execution (antivirus vendor probes, `hpacucli`, `powercfg` shell-outs).
- Server-requested `REGISTRY` section while native JSON is primary.
- pciids/usb.ids local name databases (prefer IDs; GLPI resolves names).
- Exact item-count equality when Windows APIs expose different transient data.
- Partial-inventory `full-inventory-postpone` (defer with a recorded decision).

## How to re-check

```sh
# host
cd dotnet-glpi-agent
make publish
# on Windows packager / guest: package-windows, then
cd test/vagrant-windows
./run-lab.sh up   # or re-run Compare-Inventories.ps1 inside the guest
```

Target thresholds for `scripts/Compare-Inventories.ps1` (encode + fail, not prose only):

- Dotnet ≥ Go on OS/BIOS/hardware/CPU/software/network/groups
- Software within 10 of Perl when Perl data is present
- New sections (ENVS/SLOTS/LICENSEINFOS) added to comparison as they land

## End-of-task parity review

After every completed Dotnet task (feature, fix, release, lab), re-run a
Claude/peer review against this file + `claude-validation-report.md` + the
Perl `Win32/` modules, and fold new gaps into the Top 10 table so nothing is
forgotten. Prefer implementing rows from Top 10 before inventing new scope.
