using System.Globalization;

using DotnetGlpiAgent.Core.Normalization;

namespace DotnetGlpiAgent.Windows.Management;

public sealed class WmiRow
{
    private readonly Dictionary<string, object?> _values;

    public WmiRow(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
    }

    public object? this[string name] => _values.TryGetValue(name, out object? value) ? value : null;

    public string? GetString(string name)
    {
        return this[name] switch
        {
            null => null,
            string value => InventoryNormalizer.CleanString(value),
            char[] value => InventoryNormalizer.CleanString(new string(value)),
            object value => InventoryNormalizer.CleanString(Convert.ToString(value, CultureInfo.InvariantCulture)),
        };
    }

    public IReadOnlyList<string> GetStrings(string name)
    {
        return this[name] switch
        {
            string[] values => values.Select(InventoryNormalizer.CleanString).Where(static value => value is not null).Select(static value => value!).ToArray(),
            object[] values => values.Select(static value => Convert.ToString(value, CultureInfo.InvariantCulture)).Select(InventoryNormalizer.CleanString).Where(static value => value is not null).Select(static value => value!).ToArray(),
            object value when GetString(name) is { } single => [single],
            _ => [],
        };
    }

    public uint? GetUInt32(string name) => ConvertNumber<uint>(this[name], Convert.ToUInt32);

    public ulong? GetUInt64(string name) => ConvertNumber<ulong>(this[name], Convert.ToUInt64);

    public IReadOnlyList<ushort> GetUInt16s(string name)
    {
        return this[name] switch
        {
            ushort[] values => values,
            Array values => values.Cast<object>().Select(static value => Convert.ToUInt16(value, CultureInfo.InvariantCulture)).ToArray(),
            object value => [Convert.ToUInt16(value, CultureInfo.InvariantCulture)],
            _ => [],
        };
    }

    public bool? GetBoolean(string name)
    {
        return this[name] switch
        {
            bool value => value,
            string value => InventoryNormalizer.NormalizeBoolean(value),
            null => null,
            object value => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
        };
    }

    public DateTimeOffset? GetDateTime(string name) => InventoryNormalizer.NormalizeDate(GetString(name));

    private static T? ConvertNumber<T>(object? value, Func<object, IFormatProvider, T> converter)
        where T : struct
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return converter(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
