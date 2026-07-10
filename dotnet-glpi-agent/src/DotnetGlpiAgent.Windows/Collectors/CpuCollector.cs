using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class CpuCollector : WindowsCollectorBase
{
    private static readonly string[] Properties =
    [
        "Architecture",
        "CurrentClockSpeed",
        "DeviceID",
        "Manufacturer",
        "MaxClockSpeed",
        "Name",
        "NumberOfCores",
        "NumberOfLogicalProcessors",
        "ProcessorId",
        "SerialNumber",
        "SocketDesignation",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public CpuCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-cpu";

    public override InventoryCategory Category => InventoryCategory.Cpu;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> rows = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_Processor", Properties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        CpuInfo[] cpus = rows.Select(Map).OrderBy(static cpu => cpu.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        return new InventoryContribution { Source = Name, Cpus = cpus };
    }

    public static CpuInfo Map(WmiRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string id = InventoryNormalizer.CleanIdentity(row.GetString("DeviceID"))
            ?? InventoryNormalizer.CleanIdentity(row.GetString("ProcessorId"))
            ?? InventoryNormalizer.StableKey(row.GetString("SocketDesignation"), row.GetString("Name"));

        return new CpuInfo(
            id,
            InventoryNormalizer.CleanString(row.GetString("Name")),
            InventoryNormalizer.CleanIdentity(row.GetString("Manufacturer")),
            MapArchitecture(row.GetUInt32("Architecture")),
            InventoryNormalizer.CleanString(row.GetString("SocketDesignation")),
            row.GetUInt32("NumberOfCores"),
            row.GetUInt32("NumberOfLogicalProcessors"),
            row.GetUInt32("CurrentClockSpeed"),
            row.GetUInt32("MaxClockSpeed"),
            InventoryNormalizer.CleanIdentity(row.GetString("SerialNumber")));
    }

    private static string? MapArchitecture(uint? value)
    {
        return value switch
        {
            0 => "x86",
            5 => "arm",
            9 => "x86_64",
            12 => "arm64",
            null => null,
            _ => $"unknown-{value}",
        };
    }
}
