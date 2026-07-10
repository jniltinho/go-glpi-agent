using System.Text.Json;

namespace DotnetGlpiAgent.Core.Identity;

public interface IIdentityImporter
{
    ValueTask<IdentityState?> ImportAsync(
        string path,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityImporter : IIdentityImporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask<IdentityState?> ImportAsync(
        string path,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        MigrationDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<MigrationDocument>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new IdentityStateException("The identity migration file is not valid JSON.", exception);
        }

        if (document is null)
        {
            throw new IdentityStateException("The identity migration file is empty.");
        }

        Guid? agentId = ParseAgentId(document.AgentId);
        string? deviceId = IdentityStore.NormalizeDeviceId(document.DeviceId);
        if (agentId is null && deviceId is null)
        {
            throw new IdentityStateException("The identity migration file contains no valid agentid or deviceid.");
        }

        return new IdentityState(agentId ?? Guid.Empty, deviceId ?? string.Empty, createdAtUtc.ToUniversalTime());
    }

    internal static Guid? ParseAgentId(string? value)
    {
        return Guid.TryParse(value, out Guid identifier) && identifier != Guid.Empty
            ? identifier
            : null;
    }

    private sealed record MigrationDocument
    {
        public string? AgentId { get; init; }

        public string? DeviceId { get; init; }
    }
}
