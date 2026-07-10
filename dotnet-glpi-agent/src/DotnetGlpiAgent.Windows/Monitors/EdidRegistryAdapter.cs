using System.Globalization;
using System.Text;
using DotnetGlpiAgent.Core.Collection;
using Microsoft.Win32;

namespace DotnetGlpiAgent.Windows.Monitors;

public sealed record MonitorDataSnapshot(
    string Id,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    uint? HorizontalPixels,
    uint? VerticalPixels,
    DateTimeOffset? ManufactureDate);

public interface IEdidRegistryAdapter
{
    ValueTask<IReadOnlyList<MonitorDataSnapshot>> EnumerateAsync(CancellationToken cancellationToken);
}

public sealed class EdidRegistryAdapter : IEdidRegistryAdapter
{
    private const string DisplayPath = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";

    public async ValueTask<IReadOnlyList<MonitorDataSnapshot>> EnumerateAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(
            () => Enumerate(cancellationToken),
            CancellationToken.None).ConfigureAwait(false);
    }

    private static List<MonitorDataSnapshot> Enumerate(CancellationToken cancellationToken)
    {
        try
        {
            using RegistryKey? display = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(DisplayPath, false);
            if (display is null)
            {
                return [];
            }

            var result = new List<MonitorDataSnapshot>();
            foreach (string vendorProduct in display.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using RegistryKey? vendor = display.OpenSubKey(vendorProduct, false);
                if (vendor is null)
                {
                    continue;
                }

                foreach (string instance in vendor.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using RegistryKey? parameters = vendor.OpenSubKey($@"{instance}\Device Parameters", false);
                    if (parameters?.GetValue("EDID", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is byte[] edid
                        && EdidParser.Parse($@"DISPLAY\{vendorProduct}\{instance}", edid) is { } monitor)
                    {
                        result.Add(monitor);
                    }
                }
            }

            return result;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CollectorFailureException(
                DotnetGlpiAgent.Core.Inventory.CollectionState.AccessDenied,
                "monitor-registry-access-denied",
                exception.Message);
        }
    }
}

public static class EdidParser
{
    private const int MinimumEdidLength = 128;

    public static MonitorDataSnapshot? Parse(string id, ReadOnlySpan<byte> edid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (edid.Length < MinimumEdidLength
            || !edid[..8].SequenceEqual(new byte[] { 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 }))
        {
            return null;
        }

        string? manufacturer = DecodeManufacturer(edid[8], edid[9]);
        string productCode = ((ushort)(edid[10] | (edid[11] << 8))).ToString("X4", CultureInfo.InvariantCulture);
        uint numericSerial = (uint)(edid[12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24));
        string? model = null;
        string? serial = null;
        uint? horizontal = null;
        uint? vertical = null;

        for (int offset = 54; offset + 17 < MinimumEdidLength; offset += 18)
        {
            bool descriptor = edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 2] == 0;
            if (descriptor)
            {
                string? text = DecodeDescriptorText(edid.Slice(offset + 5, 13));
                if (edid[offset + 3] == 0xfc)
                {
                    model = text;
                }
                else if (edid[offset + 3] == 0xff)
                {
                    serial = text;
                }

                continue;
            }

            if (horizontal is null)
            {
                horizontal = (uint)(edid[offset + 2] | ((edid[offset + 4] & 0xf0) << 4));
                vertical = (uint)(edid[offset + 5] | ((edid[offset + 7] & 0xf0) << 4));
            }
        }

        return new MonitorDataSnapshot(
            id,
            manufacturer,
            model ?? productCode,
            serial ?? (numericSerial == 0 ? null : numericSerial.ToString(CultureInfo.InvariantCulture)),
            horizontal,
            vertical,
            ManufactureDate(edid[16], edid[17]));
    }

    private static string? DecodeManufacturer(byte high, byte low)
    {
        int value = (high << 8) | low;
        Span<char> letters =
        [
            (char)(((value >> 10) & 0x1f) + 64),
            (char)(((value >> 5) & 0x1f) + 64),
            (char)((value & 0x1f) + 64),
        ];
        foreach (char letter in letters)
        {
            if (letter is < 'A' or > 'Z')
            {
                return null;
            }
        }

        return new string(letters);
    }

    private static string? DecodeDescriptorText(ReadOnlySpan<byte> value)
    {
        int length = value.IndexOfAny((byte)0x00, (byte)0x0a);
        ReadOnlySpan<byte> text = length < 0 ? value : value[..length];
        string result = Encoding.ASCII.GetString(text).Trim();
        return result.Length == 0 ? null : result;
    }

    private static DateTimeOffset? ManufactureDate(byte week, byte yearOffset)
    {
        if (week is 0 or > 53 || yearOffset == 0)
        {
            return null;
        }

        try
        {
            DateTime date = ISOWeek.ToDateTime(1990 + yearOffset, week, DayOfWeek.Monday);
            return new DateTimeOffset(date, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
