namespace DotnetGlpiAgent.Core.Inventory;

public sealed record HotfixInfo(
    string Id,
    string? Description,
    string? InstalledBy,
    DateTimeOffset? InstalledAt,
    string Classification);

public sealed record AppPackageInfo(
    string Id,
    string Name,
    string? Version,
    string? Publisher,
    string? Architecture,
    string? UserId,
    bool IsFramework);

public sealed record PrinterInfo(
    string Id,
    string Name,
    string? Driver,
    string? Port,
    string? Status,
    bool IsDefault,
    bool IsNetwork,
    bool IsShared);

public sealed record MonitorInfo(
    string Id,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    uint? HorizontalPixels,
    uint? VerticalPixels,
    DateTimeOffset? ManufactureDate);

public sealed record ControllerInfo(
    string Id,
    string? Name,
    string? Manufacturer,
    string? Type,
    string? Status);

public sealed record VideoAdapterInfo(
    string Id,
    string? Name,
    string? Manufacturer,
    string? DriverVersion,
    ulong? MemoryBytes,
    string? Resolution,
    string? Status);

public sealed record BatteryInfo(
    string Id,
    string? Name,
    string? Manufacturer,
    string? Chemistry,
    ulong? DesignCapacityMilliwattHours,
    ulong? FullChargeCapacityMilliwattHours,
    uint? ChargePercent,
    uint? VoltageMillivolts,
    string? Status);

public sealed record SoundDeviceInfo(
    string Id,
    string? Name,
    string? Manufacturer,
    string? Status);

public sealed record InputDeviceInfo(
    string Id,
    string? Name,
    string? Type,
    string? Status);

public sealed record PortInfo(
    string Id,
    string? Name,
    string? Type,
    string? Description,
    string? Status);

public sealed record AntivirusInfo(
    string Id,
    string Name,
    string? Version,
    string? Publisher,
    bool? Enabled,
    bool? UpToDate,
    string? Source);

public sealed record FirewallProfileInfo(
    string Id,
    string Name,
    bool Enabled,
    string? DefaultInboundAction,
    string? DefaultOutboundAction);
