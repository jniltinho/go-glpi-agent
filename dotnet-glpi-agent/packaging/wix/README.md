# MSI packaging (WiX Toolset SDK 4.0.6)

Approved toolchain: **WiX Toolset SDK 4.0.6** (MS-RL, no OSMF EULA). WiX 7 is
rejected for v1 — see `../../docs/product-decisions.md`.

## Product identity

| Field | Value |
| --- | --- |
| Product name | Dotnet GLPI Agent |
| Manufacturer | JNiltinho |
| UpgradeCode | `{474BE2D1-1A58-47E6-B9FE-700411A9E1B3}` (stable family) |
| Service name | `DotnetGlpiAgent` |
| Install dir | `%ProgramFiles%\DotnetGlpiAgent` |
| Data dir | `%ProgramData%\DotnetGlpiAgent` |

## Build (Windows only)

WiX cannot produce MSI files on Linux. On a Windows packager:

```powershell
dotnet publish ..\..\src\DotnetGlpiAgent.App\DotnetGlpiAgent.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o ..\..\artifacts\publish\win-x64

dotnet build DotnetGlpiAgent.Package.wixproj -c Release `
  -p:ProductVersion=0.1.0 -p:SkipPublishAgent=true
```

Or from the project root:

```powershell
make package-windows VERSION=0.1.0
```

Output: `artifacts/msi/dotnet-glpi-agent-<version>-win-x64.msi` plus a
`.UNSIGNED.txt` marker for development builds.

## Silent install properties (non-secret only)

```text
msiexec /i dotnet-glpi-agent-0.1.0-win-x64.msi /qn /L*v install.log ^
  SERVER="https://glpi.example/front/inventory.php" ^
  TAG="fleet-a" ^
  INSTALLDIR="C:\Program Files\DotnetGlpiAgent" ^
  STARTSERVICE=1 ^
  RUNNOW=0
```

| Property | Default | Meaning |
| --- | --- | --- |
| `SERVER` | empty | GLPI inventory URL (`http://` or `https://`) |
| `TAG` | empty | Agent tag written to first-install config |
| `INSTALLDIR` | Program Files path | Override installation directory |
| `STARTSERVICE` | `1` | Start the service after install |
| `RUNNOW` | `0` | Run one inventory cycle before install finalize |
| `PURGE` | `0` | On uninstall only: `1` deletes retained ProgramData |

## Secrets

**Do not** pass `PASSWORD`, `PROXY_PASSWORD`, or certificate passwords as MSI
properties — they appear in install logs and process command lines. After
install, as an elevated administrator:

```powershell
& "C:\Program Files\DotnetGlpiAgent\Set-AgentSecret.ps1" -Name password
# or from a tightly ACLed file that is deleted after use:
& "C:\Program Files\DotnetGlpiAgent\Set-AgentSecret.ps1" -Name password -SecretFile C:\secure\pw.txt
```

## Lifecycle

| Operation | Binaries / service | Config + identity + logs |
| --- | --- | --- |
| Clean install | Installed; service delayed-auto | Seeded defaults if absent |
| Major upgrade | Replaced transactionally | Preserved |
| Repair | Restored | Preserved (`NeverOverwrite`) |
| Downgrade | Blocked | Unchanged |
| Uninstall | Removed | **Preserved** |
| Uninstall `PURGE=1` | Removed | **Deleted** (destructive) |

```text
msiexec /x {PRODUCT-CODE} /qn PURGE=1
```

## Signing (production only)

```powershell
dotnet build DotnetGlpiAgent.Package.wixproj -c Release `
  -p:ProductVersion=1.0.0 `
  -p:SignToolPath="C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe" `
  -p:SigningCertificateThumbprint="<thumbprint>" `
  -p:TimestampUrl="http://timestamp.digicert.com"
```

Development packages keep the `.UNSIGNED.txt` sidecar. Production releases must
remove that marker by signing both the executable (publish step) and the MSI.

## Elevated custom-action review

| Action | Type | Why retained | Conditions / rollback |
| --- | --- | --- | --- |
| ServiceInstall/Control | MSI tables | SCM lifecycle | Install/uninstall; transactional |
| util:ServiceConfig | WiX util | Failure recovery | Install/reinstall |
| util:RemoveFolderEx | WiX util | Explicit purge | Uninstall + `PURGE=1` only |
| RunAgentAfterInstall | CustomAction | Optional first inventory | First install + `RUNNOW=1`; `Return=ignore`; FileRef only |

No deferred DLL/script custom actions write arbitrary paths. Configuration
mutations use `IniFile` and permanent components.
