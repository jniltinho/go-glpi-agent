using System.Text.RegularExpressions;

using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed partial class DeviceCollector : WindowsCollectorBase
{
    private static readonly string[] Properties =
    [
        "DeviceID",
        "Manufacturer",
        "Name",
        "PNPClass",
        "Present",
        "Service",
        "Status",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public DeviceCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-devices";

    public override InventoryCategory Category => InventoryCategory.Pnp;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> rows = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_PnPEntity", Properties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        (UsbDeviceInfo[] usb, PnpDeviceInfo[] pnp) = Map(rows);
        return new InventoryContribution { Source = Name, UsbDevices = usb, PnpDevices = pnp };
    }

    public static (UsbDeviceInfo[] Usb, PnpDeviceInfo[] Pnp) Map(IEnumerable<WmiRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var usb = new List<UsbDeviceInfo>();
        var pnp = new List<PnpDeviceInfo>();
        foreach (WmiRow row in rows.Take(4096))
        {
            string? deviceId = InventoryNormalizer.CleanString(row.GetString("DeviceID"));
            if (deviceId is null || row.GetBoolean("Present") == false)
            {
                continue;
            }

            bool connected = row.GetBoolean("Present")
                ?? string.Equals(row.GetString("Status"), "OK", StringComparison.OrdinalIgnoreCase);
            pnp.Add(new PnpDeviceInfo(
                deviceId,
                InventoryNormalizer.CleanString(row.GetString("Name")),
                InventoryNormalizer.CleanIdentity(row.GetString("Manufacturer")),
                InventoryNormalizer.CleanString(row.GetString("PNPClass")),
                InventoryNormalizer.CleanString(row.GetString("Status")),
                connected));

            if (!deviceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) || IsHub(row))
            {
                continue;
            }

            Match match = UsbIdentifierRegex().Match(deviceId);
            string? serial = deviceId.Split('\\').Skip(2).FirstOrDefault();
            if (serial?.Contains('&', StringComparison.Ordinal) == true)
            {
                serial = null;
            }

            usb.Add(new UsbDeviceInfo(
                deviceId,
                InventoryNormalizer.CleanString(row.GetString("Name")),
                match.Success ? match.Groups["vid"].Value.ToUpperInvariant() : null,
                match.Success ? match.Groups["pid"].Value.ToUpperInvariant() : null,
                InventoryNormalizer.CleanIdentity(serial),
                InventoryNormalizer.CleanString(row.GetString("PNPClass")),
                connected));
        }

        return (
            usb.DistinctBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            pnp.DistinctBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static bool IsHub(WmiRow row)
    {
        string value = $"{row.GetString("Name")} {row.GetString("Service")}";
        return value.Contains("HUB", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("VID_(?<vid>[0-9A-F]{4}).*PID_(?<pid>[0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex UsbIdentifierRegex();
}
