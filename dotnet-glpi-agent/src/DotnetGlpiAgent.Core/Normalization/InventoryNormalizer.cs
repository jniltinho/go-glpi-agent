using System.Globalization;

namespace DotnetGlpiAgent.Core.Normalization;

public static class InventoryNormalizer
{
    private static readonly HashSet<string> PlaceholderIdentityValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "0",
        "00000000",
        "FFFFFFFF",
        "Default string",
        "None",
        "Not Applicable",
        "N/A",
        "No Asset Tag",
        "Default Asset Tag",
        "System Serial Number",
        "To Be Filled By O.E.M.",
        "Unknown",
    };

    private static readonly string[] DateFormats =
    [
        "yyyyMMdd",
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
    ];

    public static string? CleanString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string cleaned = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length == 0 ? null : cleaned;
    }

    public static string? CleanIdentity(string? value)
    {
        string? cleaned = CleanString(value);
        return cleaned is null || PlaceholderIdentityValues.Contains(cleaned) ? null : cleaned;
    }

    public static string? NormalizeArchitecture(string? value)
    {
        return CleanString(value)?.ToUpperInvariant() switch
        {
            "64-BIT" or "AMD64" or "X64" or "X86_64" => "x86_64",
            "ARM64" or "AARCH64" => "arm64",
            "ARM" => "arm",
            "32-BIT" or "I386" or "I486" or "I586" or "I686" or "X86" => "x86",
            string architecture => architecture.ToLowerInvariant(),
            null => null,
        };
    }

    public static string? NormalizeIdentifier(string? value)
    {
        string? cleaned = CleanString(value)?.Trim('{', '}');
        if (cleaned is null)
        {
            return null;
        }

        if (Guid.TryParse(cleaned, out Guid identifier))
        {
            return identifier == Guid.Empty
                ? null
                : identifier.ToString("D", CultureInfo.InvariantCulture);
        }

        return cleaned.ToUpperInvariant();
    }

    public static bool? NormalizeBoolean(string? value)
    {
        return CleanString(value)?.ToUpperInvariant() switch
        {
            "1" or "ENABLED" or "ON" or "TRUE" or "YES" => true,
            "0" or "DISABLED" or "FALSE" or "NO" or "OFF" => false,
            _ => null,
        };
    }

    public static DateTimeOffset? NormalizeDate(string? value)
    {
        string? cleaned = CleanString(value);
        if (cleaned is null)
        {
            return null;
        }

        if (cleaned.Length >= 14 && cleaned.Take(14).All(char.IsDigit)
            && DateTimeOffset.TryParseExact(
                cleaned[..14],
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out DateTimeOffset wmiDate))
        {
            return wmiDate.ToUniversalTime();
        }

        return DateTimeOffset.TryParseExact(
            cleaned,
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    public static ulong? BytesFromKibibytes(ulong? kibibytes)
    {
        if (kibibytes is null || kibibytes > ulong.MaxValue / 1024UL)
        {
            return null;
        }

        return kibibytes * 1024UL;
    }

    public static string StableKey(params string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return string.Join(
            '\u001f',
            values.Select(static value => CleanString(value)?.ToUpperInvariant() ?? string.Empty));
    }
}
