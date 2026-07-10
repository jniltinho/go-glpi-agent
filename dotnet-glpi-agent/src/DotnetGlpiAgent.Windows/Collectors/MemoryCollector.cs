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
        var diagnostics = new List<SourceDiagnostic>();
        (IReadOnlyList<WmiRow> rows, SourceDiagnostic? modulesDiagnostic) = await WmiQueryHelpers.TryQueryAsync(
            _wmi,
            new WmiQuery(@"\\.\root\cimv2", "Win32_PhysicalMemory", ModuleProperties, Timeout: Timeout),
            $"{Name}:Win32_PhysicalMemory",
            cancellationToken).ConfigureAwait(false);
        if (modulesDiagnostic is not null)
        {
            diagnostics.Add(modulesDiagnostic);
        }

        (IReadOnlyList<WmiRow> arrays, SourceDiagnostic? arrayDiagnostic) = await WmiQueryHelpers.TryQueryAsync(
            _wmi,
            new WmiQuery(@"\\.\root\cimv2", "Win32_PhysicalMemoryArray", ["MemoryDevices"], Timeout: Timeout),
            $"{Name}:Win32_PhysicalMemoryArray",
            cancellationToken).ConfigureAwait(false);
        if (arrayDiagnostic is not null)
        {
            diagnostics.Add(arrayDiagnostic);
        }

        uint totalSlots = arrays.Aggregate(0U, static (sum, row) => checked(sum + row.GetUInt32("MemoryDevices").GetValueOrDefault()));
        MemoryModuleInfo[] modules = Map(rows, totalSlots);

        // VirtualBox/Server Core may expose no Win32_PhysicalMemory rows even when
        // total RAM is known; emit a single synthetic populated module so MEMORIES
        // is not empty (matches observed official-agent fallback behavior).
        if (modules.All(static module => module.IsEmptySlot))
        {
            (IReadOnlyList<WmiRow> systems, SourceDiagnostic? systemDiagnostic) = await WmiQueryHelpers.TryQueryAsync(
                _wmi,
                new WmiQuery(@"\\.\root\cimv2", "Win32_ComputerSystem", ["TotalPhysicalMemory"], Timeout: Timeout),
                $"{Name}:Win32_ComputerSystem",
                cancellationToken).ConfigureAwait(false);
            if (systemDiagnostic is not null)
            {
                diagnostics.Add(systemDiagnostic);
            }

            ulong? total = systems.Select(static row => row.GetUInt64("TotalPhysicalMemory")).FirstOrDefault(static value => value is > 0);
            if (total is > 0)
            {
                modules =
                [
                    new MemoryModuleInfo(
                        "system-total",
                        null,
                        "System Memory",
                        total,
                        null,
                        null,
                        null,
                        null,
                        null,
                        false),
                    .. modules,
                ];
            }
        }

        return new InventoryContribution
        {
            Source = Name,
            MemoryModules = modules,
            Diagnostics = diagnostics,
        };
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
