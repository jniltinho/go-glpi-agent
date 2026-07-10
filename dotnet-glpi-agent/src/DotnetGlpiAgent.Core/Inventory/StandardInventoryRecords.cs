namespace DotnetGlpiAgent.Core.Inventory;

public sealed record StorageDeviceInfo(
    string Id,
    string? Name,
    string? Model,
    string? Manufacturer,
    string? SerialNumber,
    string? FirmwareVersion,
    ulong? CapacityBytes,
    string? InterfaceType,
    string? MediaType,
    string? Controller,
    bool? Removable);

public sealed record VolumeInfo(
    string Id,
    string? Name,
    string? MountPoint,
    string? Label,
    string? FileSystem,
    string? DriveType,
    ulong? TotalBytes,
    ulong? FreeBytes,
    bool IsSystemVolume);

public sealed record NetworkAddressInfo(
    string Address,
    int? PrefixLength,
    string? Family,
    bool? DhcpEnabled);

public sealed record NetworkAdapterInfo(
    string Id,
    string? Name,
    string? Description,
    string? MacAddress,
    string? AdapterType,
    string? Status,
    ulong? SpeedBitsPerSecond,
    bool IsVirtual,
    bool? DhcpEnabled,
    IReadOnlyList<NetworkAddressInfo> Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers);

public sealed record UsbDeviceInfo(
    string Id,
    string? Name,
    string? VendorId,
    string? ProductId,
    string? SerialNumber,
    string? Class,
    bool Connected);

public sealed record PnpDeviceInfo(
    string Id,
    string? Name,
    string? Manufacturer,
    string? Class,
    string? Status,
    bool Connected);

public sealed record SoftwareInfo(
    string Id,
    string Name,
    string? Version,
    string? Publisher,
    string? Architecture,
    DateTimeOffset? InstallDate,
    ulong? EstimatedSizeBytes,
    string? Source,
    string? UserId,
    string? Url,
    string? UninstallCommand,
    bool IsSystemComponent,
    bool IsUpdate);

public sealed record UserInfo(
    string Id,
    string Name,
    string? Domain,
    string? FullName,
    bool IsLocal,
    bool Disabled);

public sealed record GroupInfo(
    string Id,
    string Name,
    string? Domain,
    bool IsLocal,
    IReadOnlyList<string> MemberIds);

public sealed record SessionInfo(
    string Id,
    string UserName,
    string? Domain,
    string? SessionType,
    DateTimeOffset? StartedAt,
    bool Active);

public sealed record ProcessInfo(
    int ProcessId,
    string Name,
    string? Owner,
    string? CommandLine,
    DateTimeOffset? StartedAt,
    ulong? WorkingSetBytes);
