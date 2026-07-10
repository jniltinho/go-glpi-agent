namespace DotnetGlpiAgent.Core.Inventory;

public sealed record OperatingSystemInfo(
    string Name,
    string? Edition,
    string Version,
    string Build,
    string? DisplayVersion,
    string? KernelVersion,
    string Architecture,
    string Hostname,
    string? Domain,
    DateTimeOffset? BootTime,
    DateTimeOffset? InstallDate,
    string? TimeZone,
    int? Ubr = null);

public sealed record HardwareInfo(
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? SystemUuid,
    string? AssetTag,
    string? ChassisType,
    string? BaseboardManufacturer,
    string? BaseboardProduct,
    string? BaseboardSerialNumber,
    ulong? TotalMemoryBytes,
    ulong? SwapTotalBytes);

public sealed record BiosInfo(
    string? Manufacturer,
    string? Name,
    string? Version,
    string? SerialNumber,
    DateTimeOffset? ReleaseDate,
    string? SmBiosVersion,
    bool? Uefi);

public sealed record CpuInfo(
    string Id,
    string? Name,
    string? Manufacturer,
    string? Architecture,
    string? Socket,
    uint? Cores,
    uint? LogicalProcessors,
    uint? CurrentClockMhz,
    uint? MaxClockMhz,
    string? SerialNumber);

public sealed record MemoryModuleInfo(
    string Id,
    string? BankLabel,
    string? DeviceLocator,
    ulong? CapacityBytes,
    uint? SpeedMhz,
    string? MemoryType,
    string? Manufacturer,
    string? PartNumber,
    string? SerialNumber,
    bool IsEmptySlot);
