using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class PeripheralCollector : WindowsCollectorBase
{
    private static readonly string[] DeviceProperties = ["DeviceID", "Manufacturer", "Name", "PNPDeviceID", "Status"];
    private static readonly string[] VideoProperties =
    [
        "AdapterCompatibility",
        "AdapterRAM",
        "CurrentHorizontalResolution",
        "CurrentVerticalResolution",
        "DeviceID",
        "DriverVersion",
        "Name",
        "PNPDeviceID",
        "Status",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public PeripheralCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-peripherals";

    public override InventoryCategory Category => InventoryCategory.Controller;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> video = await QueryAsync("Win32_VideoController", VideoProperties, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> sound = await QueryAsync("Win32_SoundDevice", DeviceProperties, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> keyboards = await QueryAsync("Win32_Keyboard", DeviceProperties, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> pointing = await QueryAsync("Win32_PointingDevice", DeviceProperties, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> serial = await QueryAsync("Win32_SerialPort", DeviceProperties, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> parallel = await QueryAsync("Win32_ParallelPort", DeviceProperties, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> modems = await QueryAsync("Win32_POTSModem", DeviceProperties, cancellationToken).ConfigureAwait(false);

        return new InventoryContribution
        {
            Source = Name,
            VideoAdapters = video.Select(MapVideo).Where(static item => item is not null).Select(static item => item!).OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
            SoundDevices = sound.Select(MapSound).Where(static item => item is not null).Select(static item => item!).OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
            InputDevices = keyboards.Select(static row => MapInput(row, "Keyboard"))
                .Concat(pointing.Select(static row => MapInput(row, "Pointing Device")))
                .Where(static item => item is not null).Select(static item => item!)
                .DistinctBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
            Ports = serial.Select(static row => MapPort(row, "Serial"))
                .Concat(parallel.Select(static row => MapPort(row, "Parallel")))
                .Concat(modems.Select(static row => MapPort(row, "Modem")))
                .Where(static item => item is not null).Select(static item => item!)
                .DistinctBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    public static VideoAdapterInfo? MapVideo(WmiRow row)
    {
        string? id = Identity(row);
        if (id is null)
        {
            return null;
        }

        uint? width = row.GetUInt32("CurrentHorizontalResolution");
        uint? height = row.GetUInt32("CurrentVerticalResolution");
        return new VideoAdapterInfo(
            id,
            InventoryNormalizer.CleanString(row.GetString("Name")),
            InventoryNormalizer.CleanIdentity(row.GetString("AdapterCompatibility")),
            InventoryNormalizer.CleanString(row.GetString("DriverVersion")),
            row.GetUInt64("AdapterRAM"),
            width is not null && height is not null ? $"{width}x{height}" : null,
            InventoryNormalizer.CleanString(row.GetString("Status")));
    }

    public static SoundDeviceInfo? MapSound(WmiRow row)
    {
        string? id = Identity(row);
        return id is null
            ? null
            : new SoundDeviceInfo(
                id,
                InventoryNormalizer.CleanString(row.GetString("Name")),
                InventoryNormalizer.CleanIdentity(row.GetString("Manufacturer")),
                InventoryNormalizer.CleanString(row.GetString("Status")));
    }

    public static InputDeviceInfo? MapInput(WmiRow row, string type)
    {
        string? id = Identity(row);
        return id is null
            ? null
            : new InputDeviceInfo(
                id,
                InventoryNormalizer.CleanString(row.GetString("Name")),
                type,
                InventoryNormalizer.CleanString(row.GetString("Status")));
    }

    public static PortInfo? MapPort(WmiRow row, string type)
    {
        string? id = Identity(row);
        return id is null
            ? null
            : new PortInfo(
                id,
                InventoryNormalizer.CleanString(row.GetString("Name")),
                type,
                InventoryNormalizer.CleanString(row.GetString("Manufacturer")),
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

    private static string? Identity(WmiRow row)
    {
        return InventoryNormalizer.CleanString(row.GetString("PNPDeviceID"))
            ?? InventoryNormalizer.CleanString(row.GetString("DeviceID"))
            ?? InventoryNormalizer.CleanString(row.GetString("Name"));
    }
}
