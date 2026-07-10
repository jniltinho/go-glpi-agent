using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class BatteryCollector : WindowsCollectorBase
{
    private static readonly string[] Properties =
    [
        "BatteryStatus",
        "Chemistry",
        "DesignCapacity",
        "DesignVoltage",
        "DeviceID",
        "EstimatedChargeRemaining",
        "FullChargeCapacity",
        "Manufacturer",
        "Name",
        "Status",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public BatteryCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-batteries";

    public override InventoryCategory Category => InventoryCategory.Battery;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> rows = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_Battery", Properties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        return new InventoryContribution
        {
            Source = Name,
            Batteries = rows.Select(Map).Where(static item => item is not null).Select(static item => item!).OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    public static BatteryInfo? Map(WmiRow row)
    {
        string? id = InventoryNormalizer.CleanString(row.GetString("DeviceID"))
            ?? InventoryNormalizer.CleanString(row.GetString("Name"));
        if (id is null)
        {
            return null;
        }

        return new BatteryInfo(
            id,
            InventoryNormalizer.CleanString(row.GetString("Name")),
            InventoryNormalizer.CleanIdentity(row.GetString("Manufacturer")),
            MapChemistry(row.GetUInt32("Chemistry")),
            row.GetUInt64("DesignCapacity"),
            row.GetUInt64("FullChargeCapacity"),
            row.GetUInt32("EstimatedChargeRemaining"),
            row.GetUInt32("DesignVoltage"),
            MapStatus(row.GetUInt32("BatteryStatus"), row.GetString("Status")));
    }

    private static string? MapChemistry(uint? value)
    {
        return value switch
        {
            1 => "Other",
            2 => "Unknown",
            3 => "Lead Acid",
            4 => "Nickel Cadmium",
            5 => "Nickel Metal Hydride",
            6 => "Lithium-ion",
            7 => "Zinc Air",
            8 => "Lithium Polymer",
            null => null,
            _ => $"Other ({value})",
        };
    }

    private static string? MapStatus(uint? value, string? fallback)
    {
        return value switch
        {
            1 => "Discharging",
            2 => "AC",
            3 => "Fully Charged",
            4 => "Low",
            5 => "Critical",
            6 => "Charging",
            7 => "Charging and High",
            8 => "Charging and Low",
            9 => "Charging and Critical",
            10 => "Undefined",
            11 => "Partially Charged",
            _ => InventoryNormalizer.CleanString(fallback),
        };
    }
}
