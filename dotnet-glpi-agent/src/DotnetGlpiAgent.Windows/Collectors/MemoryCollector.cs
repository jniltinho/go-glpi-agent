using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class MemoryCollector : WindowsCollectorBase
{
    private static readonly string[] ModuleProperties =
    [
        "BankLabel",
        "Capacity",
        "ConfiguredClockSpeed",
        "DeviceLocator",
        "Manufacturer",
        "PartNumber",
        "SerialNumber",
        "SMBIOSMemoryType",
        "Speed",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public MemoryCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-memory";

    public override InventoryCategory Category => InventoryCategory.Memory;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> rows = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_PhysicalMemory", ModuleProperties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> arrays = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_PhysicalMemoryArray", ["MemoryDevices"], Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        uint totalSlots = arrays.Aggregate(0U, static (sum, row) => checked(sum + row.GetUInt32("MemoryDevices").GetValueOrDefault()));
        MemoryModuleInfo[] modules = Map(rows, totalSlots);
        return new InventoryContribution { Source = Name, MemoryModules = modules };
    }

    public static MemoryModuleInfo[] Map(IEnumerable<WmiRow> rows, uint totalSlots)
    {
        ArgumentNullException.ThrowIfNull(rows);
        MemoryModuleInfo[] installed = rows
            .Select(MapModule)
            .OrderBy(static module => module.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int emptyCount = Math.Max(0, checked((int)totalSlots) - installed.Length);
        MemoryModuleInfo[] empty = Enumerable.Range(1, emptyCount)
            .Select(static index => new MemoryModuleInfo(
                $"empty-slot-{index:D2}",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                true))
            .ToArray();
        return [.. installed, .. empty];
    }

    private static MemoryModuleInfo MapModule(WmiRow row)
    {
        string? locator = InventoryNormalizer.CleanString(row.GetString("DeviceLocator"));
        string? bank = InventoryNormalizer.CleanString(row.GetString("BankLabel"));
        string id = InventoryNormalizer.StableKey(locator, bank, row.GetString("SerialNumber"));

        return new MemoryModuleInfo(
            id,
            bank,
            locator,
            row.GetUInt64("Capacity"),
            row.GetUInt32("ConfiguredClockSpeed") ?? row.GetUInt32("Speed"),
            MapMemoryType(row.GetUInt32("SMBIOSMemoryType")),
            InventoryNormalizer.CleanIdentity(row.GetString("Manufacturer")),
            InventoryNormalizer.CleanIdentity(row.GetString("PartNumber")),
            InventoryNormalizer.CleanIdentity(row.GetString("SerialNumber")),
            false);
    }

    private static string? MapMemoryType(uint? value)
    {
        return value switch
        {
            18 => "DDR",
            19 => "DDR2",
            20 => "DDR2 FB-DIMM",
            24 => "DDR3",
            26 => "DDR4",
            30 => "LPDDR4",
            34 => "DDR5",
            35 => "LPDDR5",
            null or 0 => null,
            _ => $"Other ({value})",
        };
    }
}
