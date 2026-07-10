namespace DotnetGlpiAgent.Core.Inventory;

/// <summary>
/// Contains protocol-neutral values produced by one collector.
/// </summary>
public sealed record InventoryContribution
{
    public required string Source { get; init; }

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = [];

    public OperatingSystemInfo? OperatingSystem { get; init; }

    public HardwareInfo? Hardware { get; init; }

    public BiosInfo? Bios { get; init; }

    public IReadOnlyList<CpuInfo> Cpus { get; init; } = [];

    public IReadOnlyList<MemoryModuleInfo> MemoryModules { get; init; } = [];

    public IReadOnlyList<StorageDeviceInfo> StorageDevices { get; init; } = [];

    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = [];

    public IReadOnlyList<NetworkAdapterInfo> NetworkAdapters { get; init; } = [];

    public IReadOnlyList<UsbDeviceInfo> UsbDevices { get; init; } = [];

    public IReadOnlyList<PnpDeviceInfo> PnpDevices { get; init; } = [];

    public IReadOnlyList<SoftwareInfo> Software { get; init; } = [];

    public IReadOnlyList<UserInfo> Users { get; init; } = [];

    public IReadOnlyList<GroupInfo> Groups { get; init; } = [];

    public IReadOnlyList<SessionInfo> Sessions { get; init; } = [];

    public IReadOnlyList<ProcessInfo> Processes { get; init; } = [];

    public IReadOnlyList<HotfixInfo> Hotfixes { get; init; } = [];

    public IReadOnlyList<AppPackageInfo> AppPackages { get; init; } = [];

    public IReadOnlyList<PrinterInfo> Printers { get; init; } = [];

    public IReadOnlyList<MonitorInfo> Monitors { get; init; } = [];

    public IReadOnlyList<ControllerInfo> Controllers { get; init; } = [];

    public IReadOnlyList<VideoAdapterInfo> VideoAdapters { get; init; } = [];

    public IReadOnlyList<BatteryInfo> Batteries { get; init; } = [];

    public IReadOnlyList<SoundDeviceInfo> SoundDevices { get; init; } = [];

    public IReadOnlyList<InputDeviceInfo> InputDevices { get; init; } = [];

    public IReadOnlyList<PortInfo> Ports { get; init; } = [];

    public IReadOnlyList<AntivirusInfo> AntivirusProducts { get; init; } = [];

    public IReadOnlyList<FirewallProfileInfo> FirewallProfiles { get; init; } = [];
}

public sealed record SourceDiagnostic(
    string Source,
    CollectionState State,
    string Code,
    string Message);
