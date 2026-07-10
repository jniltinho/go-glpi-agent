using System.Globalization;
using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;
using DotnetGlpiAgent.Windows.Monitors;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class MonitorCollector : WindowsCollectorBase
{
    private static readonly string[] Properties =
    [
        "Active",
        "InstanceName",
        "ManufacturerName",
        "ProductCodeID",
        "SerialNumberID",
        "WeekOfManufacture",
        "YearOfManufacture",
    ];

    private readonly IWmiQueryAdapter _wmi;
    private readonly IEdidRegistryAdapter _edid;

    public MonitorCollector(
        IWmiQueryAdapter wmi,
        IEdidRegistryAdapter edid,
        IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
        _edid = edid;
    }

    public override string Name => "windows-monitors";

    public override InventoryCategory Category => InventoryCategory.Monitor;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> rows = [];
        var diagnostics = new List<SourceDiagnostic>();
        try
        {
            rows = await _wmi.QueryAsync(
                new WmiQuery(@"\\.\root\wmi", "WmiMonitorID", Properties, "Active = TRUE", Timeout),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CollectorFailureException exception)
            when (exception.State is CollectionState.Unavailable or CollectionState.AccessDenied)
        {
            diagnostics.Add(new SourceDiagnostic(Name, exception.State, exception.DiagnosticCode, exception.Message));
        }

        MonitorInfo[] monitors = MapWmi(rows);
        if (monitors.Length == 0)
        {
            IReadOnlyList<MonitorDataSnapshot> registry = await _edid.EnumerateAsync(cancellationToken).ConfigureAwait(false);
            monitors = MapEdid(registry);
        }

        if (monitors.Length == 0 && diagnostics.Count == 0)
        {
            diagnostics.Add(new SourceDiagnostic(
                Name,
                CollectionState.Unavailable,
                "monitor-not-detected",
                "Windows did not expose an active monitor."));
        }

        return new InventoryContribution { Source = Name, Monitors = monitors, Diagnostics = diagnostics };
    }

    public static MonitorInfo[] MapWmi(IEnumerable<WmiRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Where(static row => row.GetBoolean("Active") != false)
            .Select(MapWmiRow)
            .Where(static item => item is not null)
            .Select(static item => item!)
            .DistinctBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static MonitorInfo[] MapEdid(IEnumerable<MonitorDataSnapshot> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return monitors.Select(static item => new MonitorInfo(
                item.Id,
                InventoryNormalizer.CleanIdentity(item.Manufacturer),
                InventoryNormalizer.CleanIdentity(item.Model),
                InventoryNormalizer.CleanIdentity(item.SerialNumber),
                item.HorizontalPixels,
                item.VerticalPixels,
                item.ManufactureDate))
            .DistinctBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MonitorInfo? MapWmiRow(WmiRow row)
    {
        string? id = InventoryNormalizer.CleanString(row.GetString("InstanceName"));
        if (id is null)
        {
            return null;
        }

        return new MonitorInfo(
            id,
            DecodeCharacters(row.GetUInt16s("ManufacturerName")),
            DecodeCharacters(row.GetUInt16s("ProductCodeID")),
            DecodeCharacters(row.GetUInt16s("SerialNumberID")),
            null,
            null,
            ManufactureDate(row.GetUInt32("WeekOfManufacture"), row.GetUInt32("YearOfManufacture")));
    }

    private static string? DecodeCharacters(IEnumerable<ushort> values)
    {
        string result = new(values.TakeWhile(static value => value != 0).Select(static value => (char)value).ToArray());
        return InventoryNormalizer.CleanIdentity(result);
    }

    private static DateTimeOffset? ManufactureDate(uint? week, uint? year)
    {
        if (week is null or 0 or > 53 || year is null or 0 or > 9999)
        {
            return null;
        }

        try
        {
            DateTime date = ISOWeek.ToDateTime((int)year, (int)week, DayOfWeek.Monday);
            return new DateTimeOffset(date, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
