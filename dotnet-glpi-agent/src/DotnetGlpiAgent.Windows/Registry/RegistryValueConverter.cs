using System.Globalization;

using DotnetGlpiAgent.Core.Normalization;

namespace DotnetGlpiAgent.Windows.Registry;

public static class RegistryValueConverter
{
    public static string? ToString(object? value)
    {
        return value switch
        {
            null => null,
            string text => InventoryNormalizer.CleanString(text),
            string[] values => InventoryNormalizer.CleanString(string.Join(';', values)),
            byte[] values => Convert.ToHexString(values),
            object item => InventoryNormalizer.CleanString(Convert.ToString(item, CultureInfo.InvariantCulture)),
        };
    }

    public static ulong? ToUInt64(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    public static bool? ToBoolean(object? value)
    {
        return value switch
        {
            bool boolean => boolean,
            int number => number != 0,
            long number => number != 0,
            string text => InventoryNormalizer.NormalizeBoolean(text),
            _ => null,
        };
    }
}
