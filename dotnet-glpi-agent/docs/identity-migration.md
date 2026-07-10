# Identity persistence and migration

The agent stores `identity.json` in its protected state directory. The file
contains a persistent UUID v4 `agentId`, a GLPI-compatible `deviceId`, and its
creation time. Writes use a same-directory temporary file and atomic replace.
Corrupt state is preserved with a `.corrupt-*` suffix before a replacement is
created, so an identity change is visible in diagnostics.

An administrator migrates identity from the root Go agent by copying its
`FusionInventory-Agent.json` state file into the .NET agent's state directory
before the first run; the agent adopts it automatically when no `identity.json`
exists yet. A standalone migration file uses the same minimal JSON:

```json
{
  "agentid": "4db2772d-97d0-47c3-bfe1-597e4e65bcaf",
  "deviceid": "WINDOWS-SRV-2026-07-10-00-00-00"
}
```

Migration is used only when no valid .NET identity exists. The file is parsed
as bounded JSON data; the agent never executes Perl and never directly
deserializes Perl `Storable` dump files. If migrating from a Perl deployment,
export the two identifiers into this documented JSON format first.
