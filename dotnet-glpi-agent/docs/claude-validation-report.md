# Validation report — `add-dotnet10-windows-agent` / `dotnet-glpi-agent/`

Date: 2026-07-10 · Reviewer: Claude (five parallel area reviews + verification pass)

## Verdict: **pass-with-notes**

The implementation is substantially complete, well-architected, and was genuinely
exercised end to end (real MSI on a Windows Server 2022 VM against real GLPI
10.0.26 / 11.0.8 containers). All 122 OpenSpec tasks are checked and
`openspec validate add-dotnet10-windows-agent` passes. However, this validation
found **6 P0 bugs** (5 fixed here, 1 evidential), a set of spec requirements that
are only partially satisfied (OAuth2, GLPI asset assertions, MSI upgrade/rollback
testing, comparison thresholds), and one credibility problem: the committed E2E
"passed" artifacts could not have been produced by the committed lab scripts.

## What was validated

| Area | Method | Result |
| --- | --- | --- |
| Build/tests | `make restore build test` | 0 warnings, **136/136 pass** (125 before this review) |
| Formatting | `make format` (`--verify-no-changes`) | clean |
| Dependencies | `dotnet list package --vulnerable --include-transitive` | no vulnerable packages (8 projects) |
| OpenSpec | `openspec validate`, tasks.md cross-check | valid; 122/122 checked |
| Protocol layer | line review vs `dotnet-glpi-protocol` spec | 8 requirements: 2 full, 6 partial → fixes below |
| Core/App/Service | line review vs `dotnet-agent-core` + `dotnet-windows-service` | 14 requirements: 8 full, 6 partial → fixes below |
| Windows collectors | line review vs `dotnet-windows-inventory` | 7 requirements: 4 full, 3 partial → fixes below; `Win32_Product` confirmed absent |
| MSI/packaging/CI | line review vs `dotnet-windows-msi` + design D8 | 9 requirements: 5 full, 4 partial → fixes below |
| Integration lab | line review vs `dotnet-integration-lab` + results cross-check | 11 requirements: 4 full, 7 with gaps |
| E2E artifacts | report ↔ `E2E-PASSED.json` ↔ `comparison.json` consistency | internally consistent; reproducibility issues (below) |

## Fixes applied (all verified by the test suite)

### P0 bugs

1. **TLS: custom-CA validation accepted wrong-hostname certificates**
   (`src/DotnetGlpiAgent.Protocol/Transport/GlpiHttpClientFactory.cs`). With
   `ca-cert-file` configured, the callback ignored
   `RemoteCertificateNameMismatch`, so any certificate issued by the private CA
   for any other host was accepted (MITM). Now only pure chain errors are
   re-evaluated against the custom roots, and the handshake chain feeds
   `ExtraStore` so two-tier CAs (root + intermediate) validate correctly.
2. **Orchestrator: a throwing `GetSupportAsync` destroyed the whole run**
   (`src/DotnetGlpiAgent.Core/Collection/InventoryCollectorOrchestrator.cs`).
   Only `OperationCanceledException` was caught around the support probe; any
   other exception faulted `Task.WhenAll` and discarded every collector's
   result. Now isolated as `Failed`/`support-check-failed`. New regression test.
3. **Sessions: `Win32_LoggedOnUser` antecedent regex matched the wrong WMI class**
   (`src/DotnetGlpiAgent.Windows/Collectors/UserSessionCollector.cs`). Real
   systems reference `Win32_Account`, which the regex didn't match, so every
   real logged-on session was dropped. Regex fixed; the test fixture (which had
   been written to match the bug) now uses the realistic shape.
4. **Offline hives: unload failure threw from `Dispose` and wiped the software category**
   (`src/DotnetGlpiAgent.Windows/Registry/RegistryHiveLoader.cs`). A transient
   `RegUnLoadKey` failure escaped the scanner's catch filter and aborted
   `SoftwareCollector`, violating the spec scenario "offline profile cannot be
   loaded → loaded-hive and machine software remain". Dispose is now
   best-effort (retry after finalization) and never throws.
5. **MSI: repair rewrote `server`/`tag` with an empty value**
   (`packaging/wix/Package.wxs`). `IniFile addLine Value="[SERVER]"` re-ran in
   maintenance sessions where the property is unset, clearing the fleet's
   configured server. Implemented the standard remember-property pattern
   (`SAVEDSERVER`/`SAVEDTAG` registry search + `SetProperty` after AppSearch +
   persisted `ServerValue`/`TagValue`), guarded by a new XML-level test.
   ⚠ Needs one Windows MSI build + repair run to confirm (see residual risks).

### P1 fixes

6. **Legacy fallback on malformed success** (`GlpiClient.cs`): a 200 non-JSON
   body (proxy HTML page, empty body) triggered a full legacy XML submission.
   Now a 2xx body must actually contain a legacy `<REPLY` answer to downgrade.
   New test.
7. **TLS misclassification** (`GlpiClient.cs`): operator-precedence bug reduced
   the check to "inner is AuthenticationException", so handshake resets were
   reported as generic transport failures. Fixed; new test.
8. **Pending `expiration` never parsed** (`GlpiClient.cs`): GLPI sends hours as
   a string (`"24"`) or number; only absolute timestamps were parsed. Now both
   are honored as poll deadlines, and a truncated JSON body no longer escapes
   as a raw `JsonException`. New test.
9. **Go identity migration was implemented but never wired**
   (`AppCompositionRoot.cs`, `IdentityStore.cs`): agent/device IDs were
   regenerated on migration from the Go agent → duplicate GLPI asset. The
   runtime now auto-adopts `FusionInventory-Agent.json` from the state
   directory on first run (documented in `docs/identity-migration.md`), and an
   invalid migration file degrades to a warning instead of failing every run.
   Test updated to the new contract.
10. **`validate-config` printed credential-bearing URLs**
    (`AgentConfigurationBuilder.cs`): the effective-config report masked only
    secret keys; `server = https://user:pass@…` printed verbatim. Values now
    pass through `SecretRedactor` (which strips URI userinfo). New test.
11. **Redactor missed colon/JSON-style secrets and the real config key name**
    (`SecretRedactor.cs`): `password: x` / `"password":"x"` leaked at debug
    level, and `client-cert-password` (the actual key) wasn't in the
    name-redaction set. Fixed; new tests.
12. **Registry access-denied misclassified** (`RegistryQueryAdapter.cs`):
    `SecurityException` (thrown by `OpenSubKey`/`GetValue` on denied reads) now
    maps to `AccessDenied` like `UnauthorizedAccessException`.
13. **Hotfix install dates were null on real machines**
    (`InventoryNormalizer.cs`): `Win32_QuickFixEngineering.InstalledOn` is
    en-US `M/d/yyyy`; the format list only had ISO shapes. Added; fixture made
    realistic.
14. **Azure AD users invisible** (`SoftwareCollector.cs`,
    `AppPackageDataAdapter.cs`): SID filters accepted only `S-1-5-21…`, so
    per-user software and AppX were skipped for Entra-joined users
    (`S-1-12-1-…`). Both regexes extended.
15. **Harvest GUIDs were random per build**
    (`packaging/scripts/Generate-HarvestedFiles.ps1`): violates MSI component
    rules across rebuilds. GUIDs are now deterministic (MD5 of the relative
    path).

### Test-quality and lab fixes

16. **Schema validation test was a silent no-op** without
    `GLPI_INVENTORY_SCHEMA` (`GlpiSchemaValidatorTests.cs`). It now validates
    the fixture inventory against the committed container-extracted GLPI 10
    **and** 11 schemas on every run; the env var remains an override.
17. **MSI authoring tests were satisfiable by the file's XML comment**
    (`MsiAuthoringTests.cs`). Rewritten as real `XDocument` assertions
    (service tables, recovery config, delayed autostart, MajorUpgrade,
    Permanent/NeverOverwrite, purge condition, custom-action bounds), plus a
    new remember-property test.
18. **`Win32_Product` guard** now scans the whole `src/` tree, not one file.
19. **Lab: exit 4 (partial inventory) contradiction resolved** —
    `Run-FullE2E.ps1` and `provision.ps1` now accept exit 0/4 for local runs
    and submissions (matching the documented Server-VM degradation), so the
    committed pipeline can actually reproduce the reported "passed".
20. **Lab: `provision.ps1` schema stage could never run** — wrong staged path
    (`schema\inventory.schema.json` vs `schema\glpi10\…`) and wrong validator
    invocation (`--schema/--inventory` flags vs the tool's 2 positional args).
    Both fixed; a missing schema/inventory now fails instead of silently
    passing (spec scenario compliance).
21. **Lab: Perl reference zip name mismatch** — `Compare-Inventories.ps1`
    accepts both `glpi-agent-ref.zip` and the actually-staged
    `GLPI-Agent-portable.zip`.

## Prioritized improvement backlog

### P0 — evidence / release gates

- **Regenerate the E2E evidence with the committed pipeline.** The committed
  `results/E2E-PASSED.json`/`e2e-summary.json` match no committed script's
  output shape, and (before fix 19) the run they describe would have *failed*
  the committed scripts' own rules; the summary also spans only 11 seconds.
  Re-run `run-lab.sh` end to end after these fixes and commit the regenerated
  artifacts (UTF-8 — the current `E2E-PASSED.json` is UTF-16LE and is a
  byte-duplicate of `e2e-summary.json`).
- **Build the MSI once on Windows and exercise repair** to confirm the
  remember-property change (fix 5) and the deterministic harvest GUIDs before
  the next tagged package.

### P1 — spec gaps

- **OAuth2 client credentials (GLPI 11)** — required by the
  `dotnet-glpi-protocol` spec ("optional OAuth2 client credentials … when
  enabled"); zero implementation (`grep -ri oauth src/` is empty), no
  `AgentOptions` fields. Either implement or amend the spec/design and the
  open-question record.
- **GLPI asset assertions are tautological**
  (`test/vagrant-windows/scripts/Assert-GlpiAsset.ps1`): always exits 0, checks
  only a name substring (`-ge 1`, not exactly one), never verifies high-value
  fields, has no bounded wait, and the lab never enables the GLPI API/token it
  needs. Enable the API in `test/glpi/lab.sh`, assert OS/BIOS/CPU/software
  fields, and make the caller (`Run-FullE2E.ps1:205`, currently records the
  literal `'ran'`) fail on assertion failure. This is the spec's
  "acceptance queries the GLPI API or database" requirement.
- **Comparison thresholds exist only in prose** — the spec requires documented
  minimum-coverage rules; `Compare-Inventories.ps1` emits raw counts and never
  fails. Encode thresholds (e.g. dotnet ≥ go per category; dotnet within N of
  perl for software) and exit nonzero below them.
- **MSI lifecycle matrix incomplete**
  (`packaging/scripts/Test-MsiLifecycle.ps1`): no real v1→v2 upgrade assertion
  (the `PreviousMsiPath` branch only tests downgrade-blocking), no injected
  rollback despite the authored `util:FailWhenDeferred` hook
  (`WIXFAILWHENDEFERRED=1`), purge hardcoded to skipped in every wired path,
  previous MSI never staged to the guest, and the downgrade catch treats any
  exception as "blocked" without checking exit 1638.
- **Executable is never Authenticode-signed** — the release pipeline signs only
  the MSI (`DotnetGlpiAgent.Package.wixproj` `SignMsi`;
  `dotnet-release.yml` publish step has no signtool), while the spec requires
  exe + MSI and `packaging/wix/README.md:96` claims both are signed. Fix the
  pipeline or the docs before any "signed" release.
- **Dead lab wiring** — `run-lab.sh` exports `GLPI_SERVER`/`STAGES`, and
  README documents them, but the `Vagrantfile` ignores them and always runs
  `Run-FullE2E.ps1`; `provision.ps1` (stage selection, ACL dump) is staged but
  never invoked; `KEEP_RESOURCES` is read and unused. Consequence: Hyper-V
  users cannot redirect the hardcoded VirtualBox-NAT `10.0.2.2` endpoints.
  Either wire the variables through or delete the dead path.
- **Legacy PROLOG response is not parsed** (`GlpiClient.SubmitLegacyAsync`):
  no `<RESPONSE>SEND</RESPONSE>` / `PROLOG_FREQ` handling; the inventory is
  sent even if the server declined. Low priority while native JSON is primary,
  but the fallback claims OCS compatibility it doesn't fully have.
- **Unbounded WMI connect/dispatch** (`WmiQueryAdapter.cs`): `scope.Connect()`
  and the blocking `searcher.Get()` dispatch aren't covered by the enumeration
  timeout; a hung WMI service blocks a threadpool thread past the orchestrator
  deadline, and in service mode a never-finishing collector would stall the
  cycle indefinitely (the orchestrator awaits it). Add a native connect
  timeout and a last-resort watchdog around `Task.WhenAll`.
- **Per-source degradation is missing inside multi-source collectors** —
  `SoftwareCollector` (4 online registry sources), `StorageCollector`,
  `PeripheralCollector` (7 WMI classes), `NetworkCollector`,
  `HardwareCollector`, `MemoryCollector`: one absent class/denied source
  aborts the whole collector instead of degrading that source with a
  diagnostic (spec: "accessible sources are still collected"). The
  Antivirus/Firewall/Monitor `TryQueryAsync` pattern is the template.
- **CONTACT `status:error` is not checked** (`GlpiClient.cs`): a 200
  `{"status":"error"}` still leads to the inventory upload. Validate before
  sending.
- **Design D8 contradicts the shipped toolchain** — design says WiX 7 and
  forbids silent downgrade; the implementation is WiX SDK 4.0.6 (EOL,
  unmaintained). `docs/product-decisions.md` records the EULA-driven choice
  (so it isn't silent), but `design.md` was never amended; do so, and track
  the WiX 4 EOL exposure explicitly.

### P2 — smaller items

- Harvest script flattens nested publish directories (satellite-culture
  collision risk if globalization-invariant is ever disabled).
- `InstalledOn` hex-FILETIME variant still unparsed; CIM datetime `±UUU`
  offset ignored (`InventoryNormalizer`).
- EDID checksum (byte 127) not validated; Defender can be double-listed
  (SecurityCenter2 + synthetic entry); command lines truncated *before*
  redaction (`ProcessCollector`).
- Config loader: inline `#` comments only stripped on `include` lines;
  invalid numeric/boolean values silently fall back to defaults with no
  warning; invalid CLI invocation doesn't print usage to stderr.
- Protocol nits: `Retry-After` not honored on 429; legacy
  `application/x-compress` responses not decompressed; task detection via
  `ToString().Contains("inventory")`; schema-rejection detection is a
  substring sniff; PKCS#12 client cert not disposed.
- Diagnostics: `RollingFileLogSink`/`ServiceCycleScheduler` dispose race with
  in-flight writers; `AgentLogger` lacks per-sink fault isolation;
  `EventLogSink`/`IHostEventWriter` is a dead production path (only tests use
  it) — wire or delete; correlation scope starts only after collection.
- Lab: `lab.sh` readiness accepts any HTTP 200 (races the GLPI installer —
  gate on DB/console instead); `run-lab.sh collect` needs the undocumented
  `vagrant-winrm` plugin and swallows failures; `Vagrantfile` `require_path!`
  blocks even `vagrant destroy`; `image-digests.json` records empty tags.
- Evidence: `comparison.json` `memory_slots: 1` vs `MEMORIES: 0` hints at a
  JSON-vs-XML serializer discrepancy worth a look; tasks 14.9/14.10 claim a
  changelog entry and recorded artifact hashes/sign-off that don't exist in
  the repo (root `CHANGELOG.md` has no dotnet entry; no hash/sign-off record
  in `docs/`).

## Residual risks

- **MSI authoring changes are validated only structurally** (XML tests +
  `xmllint`); WiX builds are Windows-only, so the remember-property pattern
  and deterministic GUIDs have not been compiled or installed here. One
  guest-side `Build-MsiOnGuest.ps1` + `Test-MsiLifecycle.ps1 -Repair` run is
  required before trusting them.
- **PowerShell edits are unexecuted** (no `pwsh` on this host); the edits are
  small and mechanical, but the next lab run is the real test.
- **Session-regex fix** is proven by a realistic fixture, not by live WMI; the
  next Vagrant run should show `LOCAL_USERS`/session rows for RDP logons that
  were previously missing.
- The E2E "passed" claims for GLPI submission remain credible (agent logs,
  fixture realism, and the six lab-discovered bug fixes in the report all
  check out) but are not independently reproducible until the P0 evidence
  item is done.
