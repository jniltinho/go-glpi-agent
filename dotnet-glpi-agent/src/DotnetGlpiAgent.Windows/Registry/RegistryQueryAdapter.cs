using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using Microsoft.Win32;

namespace DotnetGlpiAgent.Windows.Registry;

public sealed class RegistryKeySnapshot
{
    private readonly Dictionary<string, object?> _values;

    public RegistryKeySnapshot(string path, IReadOnlyDictionary<string, object?> values)
    {
        Path = path;
        _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
    }

    public string Path { get; }

    public IReadOnlyDictionary<string, object?> Values => _values;

    public object? this[string name] => _values.TryGetValue(name, out object? value) ? value : null;

    public string? GetString(string name) => RegistryValueConverter.ToString(this[name]);

    public ulong? GetUInt64(string name) => RegistryValueConverter.ToUInt64(this[name]);

    public bool? GetBoolean(string name) => RegistryValueConverter.ToBoolean(this[name]);
}

public interface IRegistryQueryAdapter
{
    ValueTask<RegistryKeySnapshot?> ReadKeyAsync(
        RegistryHive hive,
        RegistryView view,
        string path,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<RegistryKeySnapshot>> EnumerateSubKeysAsync(
        RegistryHive hive,
        RegistryView view,
        string path,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken);
}

public sealed class RegistryQueryAdapter : IRegistryQueryAdapter
{
    public ValueTask<RegistryKeySnapshot?> ReadKeyAsync(
        RegistryHive hive,
        RegistryView view,
        string path,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken)
    {
        return new ValueTask<RegistryKeySnapshot?>(Task.Run(
            () => ReadKey(hive, view, path, valueNames, cancellationToken),
            CancellationToken.None));
    }

    public async ValueTask<IReadOnlyList<RegistryKeySnapshot>> EnumerateSubKeysAsync(
        RegistryHive hive,
        RegistryView view,
        string path,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
            () => EnumerateSubKeys(hive, view, path, valueNames, cancellationToken),
            CancellationToken.None).ConfigureAwait(false);
    }

    private static RegistryKeySnapshot? ReadKey(
        RegistryHive hive,
        RegistryView view,
        string path,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? key = baseKey.OpenSubKey(path, false);
            return key is null ? null : Capture(path, key, valueNames, cancellationToken);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new CollectorFailureException(CollectionState.AccessDenied, "registry-access-denied", exception.Message);
        }
    }

    private static List<RegistryKeySnapshot> EnumerateSubKeys(
        RegistryHive hive,
        RegistryView view,
        string path,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? parent = baseKey.OpenSubKey(path, false);
            if (parent is null)
            {
                return [];
            }

            var snapshots = new List<RegistryKeySnapshot>();
            foreach (string name in parent.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using RegistryKey? child = parent.OpenSubKey(name, false);
                if (child is not null)
                {
                    snapshots.Add(Capture($"{path}\\{name}", child, valueNames, cancellationToken));
                }
            }

            return snapshots;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new CollectorFailureException(CollectionState.AccessDenied, "registry-access-denied", exception.Message);
        }
    }

    private static RegistryKeySnapshot Capture(
        string path,
        RegistryKey key,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in valueNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values[name] = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        return new RegistryKeySnapshot(path, values);
    }
}
