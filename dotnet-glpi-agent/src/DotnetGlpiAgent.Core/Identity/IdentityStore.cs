using System.Security.Cryptography;
using System.Text.Json;

namespace DotnetGlpiAgent.Core.Identity;

public sealed class IdentityStore
{
    public const string StateFileName = "identity.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IIdentityImporter _importer;

    public IdentityStore(IIdentityImporter? importer = null)
    {
        _importer = importer ?? new IdentityImporter();
    }

    public async ValueTask<IdentityLoadResult> LoadOrCreateAsync(
        string stateDirectory,
        string hostName,
        DateTimeOffset now,
        string? migrationFile = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentNullException.ThrowIfNull(hostName);

        Directory.CreateDirectory(stateDirectory);
        string statePath = Path.Combine(stateDirectory, StateFileName);
        await using FileStream stateLock = await AcquireLockAsync(
            Path.Combine(stateDirectory, $"{StateFileName}.lock"),
            cancellationToken).ConfigureAwait(false);
        var warnings = new List<string>();

        StateReadResult existing = await ReadStateAsync(statePath, cancellationToken).ConfigureAwait(false);
        if (existing.Identity is not null)
        {
            return new IdentityLoadResult(existing.Identity, false, false, existing.Warnings);
        }

        warnings.AddRange(existing.Warnings);
        IdentityState? imported = null;
        if (!string.IsNullOrWhiteSpace(migrationFile))
        {
            imported = await _importer.ImportAsync(migrationFile, now, cancellationToken).ConfigureAwait(false);
        }

        Guid agentId = imported?.AgentId is { } importedAgentId && importedAgentId != Guid.Empty
            ? importedAgentId
            : CreateCryptographicGuid();
        string deviceId = NormalizeDeviceId(imported?.DeviceId)
            ?? CreateDeviceId(hostName, now);
        var identity = new IdentityState(agentId, deviceId, now.ToUniversalTime());

        await WriteAtomicAsync(statePath, identity, cancellationToken).ConfigureAwait(false);
        return new IdentityLoadResult(identity, true, imported is not null, warnings);
    }

    public static string CreateDeviceId(string hostName, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(hostName);

        string shortHostName = hostName.Split('.', 2)[0].Trim();
        if (shortHostName.Length == 0)
        {
            shortHostName = "localhost";
        }

        string safeHostName = new(
            shortHostName
                .Where(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                .Take(63)
                .ToArray());
        if (safeHostName.Length == 0)
        {
            safeHostName = "localhost";
        }

        return FormattableString.Invariant($"{safeHostName}-{createdAt:yyyy-MM-dd-HH-mm-ss}");
    }

    public static Guid CreateCryptographicGuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes, true);
    }

    internal static string? NormalizeDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= 255
            && trimmed.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? trimmed
            : null;
    }

    private static async ValueTask<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<StateReadResult> ReadStateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new StateReadResult(null, []);
        }

        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            IdentityState? state = await JsonSerializer.DeserializeAsync<IdentityState>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            if (state is not null
                && state.AgentId != Guid.Empty
                && NormalizeDeviceId(state.DeviceId) is not null)
            {
                return new StateReadResult(state, []);
            }
        }
        catch (JsonException)
        {
            // The invalid file is preserved below for diagnosis.
        }

        string backup = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        File.Move(path, backup);
        return new StateReadResult(
            null,
            [$"Corrupt identity state was preserved as '{Path.GetFileName(backup)}'."]);
    }

    private static async ValueTask WriteAtomicAsync(
        string path,
        IdentityState state,
        CancellationToken cancellationToken)
    {
        string temporaryPath = $"{path}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    state,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private sealed record StateReadResult(
        IdentityState? Identity,
        IReadOnlyList<string> Warnings);
}

public sealed class IdentityStateException : Exception
{
    public IdentityStateException(string message)
        : base(message)
    {
    }

    public IdentityStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
