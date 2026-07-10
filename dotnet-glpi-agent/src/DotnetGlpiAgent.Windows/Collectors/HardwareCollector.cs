using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class HardwareCollector : WindowsCollectorBase
{
    private readonly IWmiQueryAdapter _wmi;

    public HardwareCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-hardware";

    public override InventoryCategory Category => InventoryCategory.Hardware;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        WmiRow? system = First(await QueryAsync(
            "Win32_ComputerSystem",
            ["Manufacturer", "Model", "TotalPhysicalMemory"],
            cancellationToken).ConfigureAwait(false));
        WmiRow? product = First(await QueryAsync(
            "Win32_ComputerSystemProduct",
            ["IdentifyingNumber", "Name", "UUID", "Vendor"],
            cancellationToken).ConfigureAwait(false));
        WmiRow? board = First(await QueryAsync(
            "Win32_BaseBoard",
            ["Manufacturer", "Product", "SerialNumber"],
            cancellationToken).ConfigureAwait(false));
        WmiRow? enclosure = First(await QueryAsync(
            "Win32_SystemEnclosure",
            ["ChassisTypes", "SerialNumber", "SMBIOSAssetTag"],
            cancellationToken).ConfigureAwait(false));
        WmiRow? operatingSystem = First(await QueryAsync(
            "Win32_OperatingSystem",
            ["TotalVirtualMemorySize", "TotalVisibleMemorySize"],
            cancellationToken).ConfigureAwait(false));

        if (system is null && product is null && board is null && enclosure is null)
        {
            throw new CollectorFailureException(CollectionState.Unavailable, "hardware-unavailable", "Windows hardware classes returned no rows.");
        }

        return new InventoryContribution
        {
            Source = Name,
            Hardware = Map(system, product, board, enclosure, operatingSystem),
        };
    }

    public static HardwareInfo Map(
        WmiRow? system,
        WmiRow? product,
        WmiRow? board,
        WmiRow? enclosure,
        WmiRow? operatingSystem = null)
    {
        string? serial = InventoryNormalizer.CleanIdentity(product?.GetString("IdentifyingNumber"))
            ?? InventoryNormalizer.CleanIdentity(enclosure?.GetString("SerialNumber"))
            ?? InventoryNormalizer.CleanIdentity(board?.GetString("SerialNumber"));

        IReadOnlyList<ushort>? chassisTypes = enclosure?.GetUInt16s("ChassisTypes");
        ushort? chassisType = chassisTypes?.Count > 0 ? chassisTypes[0] : null;
        ulong? visibleMemoryKib = operatingSystem?.GetUInt64("TotalVisibleMemorySize");
        ulong? virtualMemoryKib = operatingSystem?.GetUInt64("TotalVirtualMemorySize");
        ulong? swapKib = virtualMemoryKib >= visibleMemoryKib
            ? virtualMemoryKib - visibleMemoryKib
            : null;

        return new HardwareInfo(
            InventoryNormalizer.CleanIdentity(system?.GetString("Manufacturer") ?? product?.GetString("Vendor")),
            InventoryNormalizer.CleanIdentity(system?.GetString("Model") ?? product?.GetString("Name")),
            serial,
            InventoryNormalizer.NormalizeIdentifier(product?.GetString("UUID")),
            InventoryNormalizer.CleanIdentity(enclosure?.GetString("SMBIOSAssetTag")),
            MapChassisType(chassisType),
            InventoryNormalizer.CleanIdentity(board?.GetString("Manufacturer")),
            InventoryNormalizer.CleanIdentity(board?.GetString("Product")),
            InventoryNormalizer.CleanIdentity(board?.GetString("SerialNumber")),
            system?.GetUInt64("TotalPhysicalMemory")
                ?? InventoryNormalizer.BytesFromKibibytes(visibleMemoryKib),
            InventoryNormalizer.BytesFromKibibytes(swapKib));
    }

    private ValueTask<IReadOnlyList<WmiRow>> QueryAsync(
        string className,
        IReadOnlyList<string> properties,
        CancellationToken cancellationToken)
    {
        return _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", className, properties, Timeout: Timeout),
            cancellationToken);
    }

    private static WmiRow? First(IReadOnlyList<WmiRow> rows) => rows.Count > 0 ? rows[0] : null;

    private static string? MapChassisType(ushort? value)
    {
        return value switch
        {
            3 or 4 or 5 or 6 or 7 or 15 or 16 => "Desktop",
            8 or 9 or 10 or 11 or 12 or 14 or 18 or 21 => "Laptop",
            17 or 23 or 28 or 29 => "Server",
            30 or 31 or 32 => "Tablet",
            35 or 36 => "Mini PC",
            null or 0 => null,
            _ => $"Other ({value})",
        };
    }
}
