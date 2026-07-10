# Inventory parity with the official Perl GLPI Agent

## Scope

The .NET agent targets **inventory** feature parity with
`base/glpi-agent` (Windows modules under
`Task/Inventory/Win32/`). It does **not** implement non-inventory tasks
that the full Perl agent ships (deploy, network discovery, ESX, Wake-on-LAN,
remote inventory, collect). Those remain explicit non-goals of the OpenSpec
design (D non-goals).

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

## Remaining intentional differences

1. **No deploy / netdiscovery / ESX / WakeOnLan / remote inventory** — product scope.
2. **OAuth2 client credentials** — deferred after v1 in `docs/product-decisions.md`
   (spec still mentions optional OAuth2; amend or implement later).
3. **Some Server Core WMI classes fail** (batteries, monitors, keyboards on some
   images) — degraded per-source with diagnostics; inventory still accepted by GLPI.
4. **Drive count** — Perl may list CD-ROM / recovery partitions differently.

## How to re-check

```sh
# host
cd dotnet-glpi-agent
make publish
# on Windows packager / guest: package-windows, then
cd test/vagrant-windows
./run-lab.sh up   # or re-run Compare-Inventories.ps1 inside the guest
```

Thresholds enforced by `scripts/Compare-Inventories.ps1`:
- Dotnet ≥ Go on OS/BIOS/hardware/CPU/software/network/groups
- Software within 10 of Perl when Perl data is present
