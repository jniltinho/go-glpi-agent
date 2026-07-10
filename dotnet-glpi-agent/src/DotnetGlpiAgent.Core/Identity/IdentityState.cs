namespace DotnetGlpiAgent.Core.Identity;

public sealed record IdentityState(
    Guid AgentId,
    string DeviceId,
    DateTimeOffset CreatedAtUtc);

public sealed record IdentityLoadResult(
    IdentityState Identity,
    bool WasCreated,
    bool WasMigrated,
    IReadOnlyList<string> Warnings);
