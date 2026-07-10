# Dotnet GLPI Agent

Windows-first GLPI inventory agent built with .NET 10. The project combines a
typed, testable architecture with broad Windows inventory coverage and is kept
independent from the Go and Perl implementations in the parent repository.

## Layout

- `src/DotnetGlpiAgent.Core` — protocol-neutral model and orchestration.
- `src/DotnetGlpiAgent.Windows` — Windows API adapters and collectors.
- `src/DotnetGlpiAgent.Protocol` — native GLPI JSON and legacy XML transports.
- `src/DotnetGlpiAgent.App` — CLI and Windows Service composition root.
- `tests` — unit, contract, fixture, and integration tests.
- `packaging/wix` — gated MSI authoring.
- `test` — isolated Docker GLPI and Vagrant Windows laboratory.
- `docs` — architecture, operation, testing, and release decisions.

## Build and test

The .NET 10.0.301 SDK is pinned by `global.json`.

```powershell
dotnet restore DotnetGlpiAgent.sln
dotnet build DotnetGlpiAgent.sln --configuration Release --no-restore
dotnet test DotnetGlpiAgent.sln --configuration Release --no-build
dotnet publish src/DotnetGlpiAgent.App/DotnetGlpiAgent.App.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  --output artifacts/publish/win-x64
```

The normal publish is self-contained and deliberately disables trimming and
Native AOT for compatibility with Windows management APIs.

Implementation progress is tracked by the OpenSpec change
`add-dotnet10-windows-agent`.
