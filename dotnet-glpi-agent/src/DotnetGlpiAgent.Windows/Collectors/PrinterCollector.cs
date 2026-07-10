using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class PrinterCollector : WindowsCollectorBase
{
    private static readonly string[] Properties =
    [
        "Default",
        "DeviceID",
        "DriverName",
        "Name",
        "Network",
        "PortName",
        "PrinterStatus",
        "Shared",
        "Status",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public PrinterCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-printers";

    public override InventoryCategory Category => InventoryCategory.Printer;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> rows = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_Printer", Properties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        return new InventoryContribution { Source = Name, Printers = Map(rows) };
    }

    public static PrinterInfo[] Map(IEnumerable<WmiRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Select(MapPrinter)
            .Where(static printer => printer is not null)
            .Select(static printer => printer!)
            .DistinctBy(static printer => printer.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static printer => printer.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PrinterInfo? MapPrinter(WmiRow row)
    {
        string? name = InventoryNormalizer.CleanString(row.GetString("Name"));
        string? id = InventoryNormalizer.CleanString(row.GetString("DeviceID")) ?? name;
        if (id is null || name is null)
        {
            return null;
        }

        return new PrinterInfo(
            id,
            name,
            InventoryNormalizer.CleanString(row.GetString("DriverName")),
            InventoryNormalizer.CleanString(row.GetString("PortName")),
            MapStatus(row.GetUInt32("PrinterStatus"), row.GetString("Status")),
            row.GetBoolean("Default") == true,
            row.GetBoolean("Network") == true,
            row.GetBoolean("Shared") == true);
    }

    private static string? MapStatus(uint? status, string? fallback)
    {
        return status switch
        {
            1 => "Other",
            2 => "Unknown",
            3 => "Idle",
            4 => "Printing",
            5 => "Warmup",
            6 => "Stopped",
            7 => "Offline",
            _ => InventoryNormalizer.CleanString(fallback),
        };
    }
}
