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
        "VideoProcessor",
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
        var diagnostics = new List<SourceDiagnostic>();
        IReadOnlyList<WmiRow> video = await QueryClassAsync(
            "Win32_VideoController",
            VideoProperties,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> sound = await QueryClassAsync(
            "Win32_SoundDevice",
            DeviceProperties,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> keyboards = await QueryClassAsync(
            "Win32_Keyboard",
            DeviceProperties,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> pointing = await QueryClassAsync(
            "Win32_PointingDevice",
            DeviceProperties,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> serial = await QueryClassAsync(
            "Win32_SerialPort",
            DeviceProperties,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> parallel = await QueryClassAsync(
            "Win32_ParallelPort",
            DeviceProperties,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> modems = await QueryClassAsync(
            "Win32_POTSModem",
            DeviceProperties,
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        VideoAdapterInfo[] videos = video.Select(MapVideo)
            .Where(static item => item is not null)
            .Select(static item => item!)
            // Official agent skips RDP/remote display adapters.
            .Where(static item => item.Id.IndexOf("REMOTEDISPLAY", StringComparison.OrdinalIgnoreCase) < 0)
            .DistinctBy(static item => item.Name ?? item.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new InventoryContribution
        {
            Source = Name,
            VideoAdapters = videos,
            SoundDevices = sound.Select(MapSound).Where(static item => item is not null).Select(static item => item!)
                .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
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
            Diagnostics = diagnostics,
        };
    }

    public static VideoAdapterInfo? MapVideo(WmiRow row)
    {
        string? id = Identity(row);
        if (id is null)
        {
            return null;
        }

        string? name = InventoryNormalizer.CleanString(row.GetString("Name"));
        if (name is null)
        {
            return null;
        }

        uint? width = row.GetUInt32("CurrentHorizontalResolution");
        uint? height = row.GetUInt32("CurrentVerticalResolution");
        string? manufacturer = InventoryNormalizer.CleanIdentity(row.GetString("AdapterCompatibility"))
            ?? InventoryNormalizer.CleanString(row.GetString("VideoProcessor"));
        return new VideoAdapterInfo(
            id,
            name,
            manufacturer,
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

    private async ValueTask<IReadOnlyList<WmiRow>> QueryClassAsync(
        string className,
        IReadOnlyList<string> properties,
        List<SourceDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        (IReadOnlyList<WmiRow> rows, SourceDiagnostic? diagnostic) = await WmiQueryHelpers.TryQueryAsync(
            _wmi,
            new WmiQuery(@"\\.\root\cimv2", className, properties, Timeout: Timeout),
            $"{Name}:{className}",
            cancellationToken).ConfigureAwait(false);
        if (diagnostic is not null)
        {
            diagnostics.Add(diagnostic);
        }

        return rows;
    }

    private static string? Identity(WmiRow row)
    {
        return InventoryNormalizer.CleanString(row.GetString("PNPDeviceID"))
            ?? InventoryNormalizer.CleanString(row.GetString("DeviceID"))
            ?? InventoryNormalizer.CleanString(row.GetString("Name"));
    }
}
