using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class StorageCollector : WindowsCollectorBase
{
    private static readonly string[] DiskProperties =
    [
        "DeviceID",
        "FirmwareRevision",
        "Index",
        "InterfaceType",
        "Manufacturer",
        "MediaType",
        "Model",
        "PNPDeviceID",
        "SerialNumber",
        "Size",
    ];

    private static readonly string[] ControllerProperties =
    [
        "DeviceID",
        "Manufacturer",
        "Name",
        "PNPDeviceID",
        "Status",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public StorageCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-storage";

    public override InventoryCategory Category => InventoryCategory.Storage;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> disks = await QueryAsync(
            "Win32_DiskDrive",
            DiskProperties,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> scsi = await QueryAsync(
            "Win32_SCSIController",
            ControllerProperties,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> ide = await QueryAsync(
            "Win32_IDEController",
            ControllerProperties,
            cancellationToken).ConfigureAwait(false);

        return new InventoryContribution
        {
            Source = Name,
            StorageDevices = disks.Select(MapDisk)
                .OrderBy(static disk => disk.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Controllers = scsi.Select(static row => MapController(row, "SCSI/Storage"))
                .Concat(ide.Select(static row => MapController(row, "IDE/ATA")))
                .DistinctBy(static controller => controller.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static controller => controller.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    public static StorageDeviceInfo MapDisk(WmiRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string? pnpId = InventoryNormalizer.CleanString(row.GetString("PNPDeviceID"));
        string id = InventoryNormalizer.CleanString(row.GetString("DeviceID"))
            ?? pnpId
            ?? $"disk-{row.GetUInt32("Index").GetValueOrDefault()}";
        string? model = InventoryNormalizer.CleanIdentity(row.GetString("Model"));
        string? media = InventoryNormalizer.CleanString(row.GetString("MediaType"));
        string? interfaceType = InventoryNormalizer.CleanString(row.GetString("InterfaceType"));

        return new StorageDeviceInfo(
            id,
            model ?? id,
            model,
            InventoryNormalizer.CleanIdentity(row.GetString("Manufacturer")),
            InventoryNormalizer.CleanIdentity(row.GetString("SerialNumber")),
            InventoryNormalizer.CleanIdentity(row.GetString("FirmwareRevision")),
            row.GetUInt64("Size"),
            interfaceType,
            ClassifyMedia(model, media, interfaceType, pnpId),
            null,
            media?.Contains("removable", StringComparison.OrdinalIgnoreCase));
    }

    public static ControllerInfo MapController(WmiRow row, string type)
    {
        ArgumentNullException.ThrowIfNull(row);
        string id = InventoryNormalizer.CleanString(row.GetString("PNPDeviceID"))
            ?? InventoryNormalizer.CleanString(row.GetString("DeviceID"))
            ?? InventoryNormalizer.StableKey(row.GetString("Name"), type);
        return new ControllerInfo(
            id,
            InventoryNormalizer.CleanString(row.GetString("Name")),
            InventoryNormalizer.CleanIdentity(row.GetString("Manufacturer")),
            type,
            InventoryNormalizer.CleanString(row.GetString("Status")));
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

    private static string ClassifyMedia(
        string? model,
        string? media,
        string? interfaceType,
        string? pnpId)
    {
        string combined = $"{model} {media} {interfaceType} {pnpId}";
        if (combined.Contains("NVME", StringComparison.OrdinalIgnoreCase))
        {
            return "NVMe SSD";
        }

        if (combined.Contains("SSD", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("SOLID STATE", StringComparison.OrdinalIgnoreCase))
        {
            return "SSD";
        }

        if (combined.Contains("REMOVABLE", StringComparison.OrdinalIgnoreCase))
        {
            return "Removable";
        }

        return "HDD";
    }
}
