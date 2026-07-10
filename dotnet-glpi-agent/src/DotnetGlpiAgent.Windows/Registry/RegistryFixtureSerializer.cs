using System.Text.Json;

namespace DotnetGlpiAgent.Windows.Registry;

public static class RegistryFixtureSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string Serialize(IEnumerable<RegistryKeySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        RegistryFixtureEntry[] entries = snapshots
            .OrderBy(static snapshot => snapshot.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static snapshot => new RegistryFixtureEntry(snapshot.Path, snapshot.Values))
            .ToArray();
        return JsonSerializer.Serialize(entries, SerializerOptions);
    }

    public static IReadOnlyList<RegistryKeySnapshot> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        RegistryFixtureEntry[] entries = JsonSerializer.Deserialize<RegistryFixtureEntry[]>(json, SerializerOptions)
            ?? [];
        return entries.Select(static entry => new RegistryKeySnapshot(
                entry.Path,
                entry.Values.ToDictionary(
                    static pair => pair.Key,
                    static pair => ConvertElement(pair.Value),
                    StringComparer.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static object? ConvertElement(object? value)
    {
        if (value is not JsonElement element)
        {
            return value;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray().Select(static item => ConvertElement(item)).ToArray(),
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Number when element.TryGetInt64(out long number) => number,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            _ => element.GetRawText(),
        };
    }

    private sealed record RegistryFixtureEntry(
        string Path,
        IReadOnlyDictionary<string, object?> Values);
}
