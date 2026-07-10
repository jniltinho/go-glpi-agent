# Third-party notices

The project records all direct runtime, test, packaging, and build dependencies
in centrally managed project files (`Directory.Packages.props`). Release
artifacts must include an SBOM snapshot (`sbom-dotnet-packages.txt` or CycloneDX
when available) and the license notices applicable to the exact resolved
dependency versions.

## Direct dependencies (see Directory.Packages.props for pinned versions)

| Package | Role | License (upstream) |
| --- | --- | --- |
| Microsoft.Extensions.Hosting.WindowsServices | Windows Service host | MIT |
| System.Management | WMI/CIM inventory | MIT |
| Microsoft.NET.Test.Sdk / xunit / coverlet.collector | Tests | MIT / Apache-2.0 |
| NJsonSchema | Schema validation utility | MIT |
| WixToolset.Sdk 4.0.6 | MSI packaging | MS-RL |
| WixToolset.Util.wixext 4.0.6 | MSI util extension | MS-RL |

WiX Toolset **7** OSMF terms are **not** accepted for v1. The approved packaging
toolchain is WiX Toolset SDK **4.0.6** only (see `docs/product-decisions.md`).

The Perl implementations under the parent repository's `base/` directory are
GPL-licensed behavioral references. No Perl source is included in this project
or its binary artifacts.

Official GLPI Docker images used in the test laboratory are subject to their
upstream licenses; they are not redistributed as product artifacts.
