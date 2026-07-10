# Windows Service operation

The MSI installs the same `dotnet-glpi-agent.exe` used by the CLI as the `DotnetGlpiAgent` Windows Service. Its service command line is:

```text
"C:\Program Files\DotnetGlpiAgent\dotnet-glpi-agent.exe" service --config "C:\Program Files\DotnetGlpiAgent\agent.cfg"
```

The service runs as LocalSystem so it can read machine WMI, HKLM/HKU inventory, load explicitly enabled offline profile hives, and use the machine certificate store. It does not require interactive logon, desktop access, network shares, local administrator credentials in configuration, or write access outside its protected ProgramData tree.

Before hosting starts, the executable must resolve below Program Files and the configuration, includes, state, and logs must resolve below `C:\ProgramData\DotnetGlpiAgent`. Paths whose ACL grants write access to Everyone, Authenticated Users, or the built-in Users group are rejected. Administrators and SYSTEM retain full control.

The first cycle is randomized by up to five minutes unless `force` is enabled. Later intervals begin after the previous collection and submission complete, and a process-local lock prevents overlapping cycles. SCM stop and shutdown cancellation flows through collection, serialization, HTTP submission, and polling; the host has a 30-second shutdown grace period.

Service lifecycle and cycle failures at warning level or above go to the Windows Application event log through the Windows Service host. Detailed structured, redacted diagnostics rotate under `C:\ProgramData\DotnetGlpiAgent\logs`.
