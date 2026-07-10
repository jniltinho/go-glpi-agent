# Product decisions

## Stable v1 identity

- Product: **Dotnet GLPI Agent**
- Executable: `dotnet-glpi-agent.exe`
- Manufacturer: **JNiltinho**
- Windows Service name: `DotnetGlpiAgent`
- Windows Service display name: **Dotnet GLPI Agent**
- Package family upgrade code: `{474BE2D1-1A58-47E6-B9FE-700411A9E1B3}`
  (stable; authored in `packaging/wix/Package.wxs`)

## Support matrix

The intended v1 targets are Windows 10 and 11 x64 and Windows Server 2022 and
2025 x64. Windows Server 2022 is the mandatory release baseline. GLPI 10 and 11
native inventory are required. OAuth2 client credentials are deferred until
after v1; unauthenticated, proxy/basic-authenticated, and TLS client-certificate
deployment remain in scope.

The v1 protocol implementation therefore has no OAuth2 token acquisition path.
OAuth2 configuration keys are rejected as unknown instead of being silently
ignored; adding client credentials requires a later explicit product/security
decision.

## Signing ownership

The repository release maintainer owns production Authenticode signing. A
production release remains blocked until that maintainer configures a protected
code-signing certificate and a trusted timestamp provider. Development MSI and
portable artifacts must be labeled unsigned.

## Retained data

Normal uninstall preserves configuration, persistent identity, and bounded
diagnostic logs. Explicit elevated `PURGE=1` removes all retained state.

## MSI toolchain legal gate

WiX Toolset 7 binary terms are rejected for v1 because the distributor has not
accepted its OSMF EULA or any applicable maintenance fee. The approved MSI
toolchain is WiX Toolset SDK 4.0.6: its NuGet metadata declares MS-RL,
`requireLicenseAcceptance=false`, and no maintenance-fee EULA. The SDK and Util
extension are pinned to 4.0.6 and their notices are included with release
artifacts. Moving to WiX 5 or later requires a new explicit legal review.
