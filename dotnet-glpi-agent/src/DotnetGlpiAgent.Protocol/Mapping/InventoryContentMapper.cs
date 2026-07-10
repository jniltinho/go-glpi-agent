using System.Globalization;
using System.Net;
using System.Net.Sockets;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;

namespace DotnetGlpiAgent.Protocol.Mapping;

public static class InventoryContentMapper
{
    public static ProtocolContent Map(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var content = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["versionclient"] = snapshot.Identity.AgentVersion,
        };

        Add(content, "hardware", MapHardware(snapshot));
        Add(content, "bios", MapBios(snapshot));
        Add(content, "operatingsystem", MapOperatingSystem(snapshot.OperatingSystem));
        AddList(content, "cpus", snapshot.Cpus.Select(MapCpu));
        AddList(content, "memories", snapshot.MemoryModules.Where(static memory => !memory.IsEmptySlot).Select(MapMemory));
        AddList(content, "drives", snapshot.Volumes.Select(MapVolume));
        AddList(content, "storages", snapshot.StorageDevices.Select(MapStorage));
        AddList(content, "networks", snapshot.NetworkAdapters.SelectMany(MapNetwork));
        AddList(content, "softwares", MapSoftware(snapshot));
        AddList(content, "usbdevices", snapshot.UsbDevices.Select(MapUsb));
        AddList(content, "local_users", snapshot.Users.Where(static user => user.IsLocal).Select(MapLocalUser));
        AddList(content, "local_groups", snapshot.Groups.Where(static group => group.IsLocal).Select(MapLocalGroup));
        AddList(content, "users", snapshot.Sessions.Where(static session => session.Active).Select(MapSession));
        AddList(content, "processes", snapshot.Processes.Select(MapProcess));
        AddList(content, "printers", snapshot.Printers.Select(MapPrinter));
        AddList(content, "monitors", snapshot.Monitors.Select(MapMonitor));
        AddList(content, "controllers", snapshot.Controllers.Select(MapController));
        AddList(content, "videos", snapshot.VideoAdapters.Select(MapVideo));
        AddList(content, "batteries", snapshot.Batteries.Select(MapBattery));
        AddList(content, "sounds", snapshot.SoundDevices.Select(MapSound));
        AddList(content, "inputs", snapshot.InputDevices.Select(MapInput));
        AddList(content, "ports", snapshot.Ports.Where(static port => !string.Equals(port.Type, "Modem", StringComparison.OrdinalIgnoreCase)).Select(MapPort));
        AddList(content, "modems", snapshot.Ports.Where(static port => string.Equals(port.Type, "Modem", StringComparison.OrdinalIgnoreCase)).Select(MapModem));
        AddList(content, "antivirus", snapshot.AntivirusProducts.Select(MapAntivirus));
        AddList(content, "firewalls", snapshot.FirewallProfiles.Select(MapFirewall));
        if (!string.IsNullOrWhiteSpace(snapshot.Account.Tag))
        {
            AddList(content, "accountinfo", [Record(("keyname", "TAG"), ("keyvalue", snapshot.Account.Tag))]);
        }

        return new ProtocolContent(content);
    }

    private static SortedDictionary<string, object?>? MapHardware(InventorySnapshot snapshot)
    {
        HardwareInfo? hardware = snapshot.Hardware;
        OperatingSystemInfo? operatingSystem = snapshot.OperatingSystem;
        if (hardware is null && operatingSystem is null)
        {
            return null;
        }

        return Record(
            ("chassis_type", hardware?.ChassisType),
            ("memory", ToMebibytes(hardware?.TotalMemoryBytes)),
            ("name", operatingSystem?.Hostname ?? snapshot.Identity.DeviceName),
            ("swap", ToMebibytes(hardware?.SwapTotalBytes)),
            ("uuid", hardware?.SystemUuid),
            ("vmsystem", DetectVirtualization(hardware)),
            ("workgroup", operatingSystem?.Domain));
    }

    private static SortedDictionary<string, object?>? MapBios(InventorySnapshot snapshot)
    {
        HardwareInfo? hardware = snapshot.Hardware;
        BiosInfo? bios = snapshot.Bios;
        if (hardware is null && bios is null)
        {
            return null;
        }

        return Record(
            ("assettag", hardware?.AssetTag),
            ("bdate", FormatDate(bios?.ReleaseDate)),
            ("biosserial", bios?.SerialNumber),
            ("bmanufacturer", bios?.Manufacturer),
            ("bversion", bios?.Version ?? bios?.Name),
            ("mmanufacturer", hardware?.BaseboardManufacturer),
            ("mmodel", hardware?.BaseboardProduct),
            ("msn", hardware?.BaseboardSerialNumber),
            ("smanufacturer", hardware?.Manufacturer),
            ("smodel", hardware?.Model),
            ("ssn", hardware?.SerialNumber));
    }

    private static SortedDictionary<string, object?>? MapOperatingSystem(OperatingSystemInfo? operatingSystem)
    {
        return operatingSystem is null
            ? null
            : Record(
                ("arch", operatingSystem.Architecture),
                ("boot_time", FormatDateTime(operatingSystem.BootTime)),
                ("dns_domain", operatingSystem.Domain),
                ("fqdn", FullyQualifiedName(operatingSystem)),
                ("full_name", operatingSystem.Edition is null ? operatingSystem.Name : $"{operatingSystem.Name} {operatingSystem.Edition}"),
                ("install_date", FormatDateTime(operatingSystem.InstallDate)),
                ("kernel_name", "win32"),
                ("kernel_version", operatingSystem.KernelVersion ?? operatingSystem.Build),
                ("name", "Windows"),
                ("version", operatingSystem.DisplayVersion ?? operatingSystem.Version));
    }

    private static SortedDictionary<string, object?> MapCpu(CpuInfo cpu)
    {
        return Record(
            ("arch", cpu.Architecture),
            ("core", cpu.Cores),
            ("corecount", cpu.Cores),
            ("id", cpu.Id),
            ("manufacturer", cpu.Manufacturer),
            ("name", cpu.Name),
            ("serial", cpu.SerialNumber),
            ("speed", cpu.CurrentClockMhz ?? cpu.MaxClockMhz),
            ("thread", cpu.LogicalProcessors));
    }

    private static SortedDictionary<string, object?> MapMemory(MemoryModuleInfo memory)
    {
        return Record(
            ("capacity", ToMebibytes(memory.CapacityBytes)),
            ("caption", memory.DeviceLocator),
            ("description", memory.BankLabel),
            ("manufacturer", memory.Manufacturer),
            ("model", memory.PartNumber),
            ("serialnumber", memory.SerialNumber),
            ("speed", memory.SpeedMhz is null ? null : $"{memory.SpeedMhz} MT/s"),
            ("type", memory.MemoryType));
    }

    private static SortedDictionary<string, object?> MapVolume(VolumeInfo volume)
    {
        return Record(
            ("filesystem", volume.FileSystem),
            ("free", ToMebibytes(volume.FreeBytes)),
            ("label", volume.Label),
            ("letter", DriveLetter(volume.MountPoint ?? volume.Id)),
            ("systemdrive", volume.IsSystemVolume),
            ("total", ToMebibytes(volume.TotalBytes)),
            ("type", volume.MountPoint),
            ("volumn", volume.Id));
    }

    private static SortedDictionary<string, object?> MapStorage(StorageDeviceInfo storage)
    {
        return Record(
            ("description", storage.MediaType),
            ("disksize", ToMebibytes(storage.CapacityBytes)),
            ("firmware", storage.FirmwareVersion),
            ("interface", storage.InterfaceType),
            ("manufacturer", storage.Manufacturer),
            ("model", storage.Model),
            ("name", storage.Name ?? storage.Id),
            ("serial", storage.SerialNumber),
            ("type", "disk"));
    }

    private static IEnumerable<SortedDictionary<string, object?>> MapNetwork(NetworkAdapterInfo network)
    {
        NetworkAddressInfo[] addresses = network.Addresses.Count == 0
            ? [new NetworkAddressInfo(string.Empty, null, null, network.DhcpEnabled)]
            : [.. network.Addresses];
        foreach (NetworkAddressInfo address in addresses.OrderBy(static item => item.Address, StringComparer.OrdinalIgnoreCase))
        {
            bool ipv6 = string.Equals(address.Family, "InterNetworkV6", StringComparison.OrdinalIgnoreCase)
                || address.Address.Contains(':', StringComparison.Ordinal);
            (string? mask, string? subnet) = ipv6 ? (null, null) : IPv4Network(address.Address, address.PrefixLength);
            yield return Record(
                ("description", network.Description ?? network.Name ?? network.Id),
                ("ipaddress", !ipv6 ? EmptyToNull(address.Address) : null),
                ("ipaddress6", ipv6 ? EmptyToNull(address.Address) : null),
                ("ipgateway", network.Gateways.Count == 0 ? null : network.Gateways[0]),
                ("ipmask", mask),
                ("ipsubnet", subnet),
                ("mac", network.MacAddress),
                ("speed", network.SpeedBitsPerSecond is null ? null : (network.SpeedBitsPerSecond.Value / 1_000_000UL).ToString(CultureInfo.InvariantCulture)),
                ("status", MapNetworkStatus(network.Status)),
                ("type", MapNetworkType(network.AdapterType)),
                ("virtualdev", network.IsVirtual));
        }
    }

    private static IEnumerable<SortedDictionary<string, object?>> MapSoftware(InventorySnapshot snapshot)
    {
        IEnumerable<SoftwareProtocolEntry> registry = snapshot.Software.Select(static software => new SoftwareProtocolEntry(
            software.Id,
            software.Name,
            software.Version,
            software.Publisher,
            software.Architecture,
            software.InstallDate,
            software.EstimatedSizeBytes,
            software.Source,
            software.UserId,
            software.Url,
            software.UninstallCommand,
            software.IsSystemComponent,
            software.IsUpdate ? "Update" : null));
        IEnumerable<SoftwareProtocolEntry> appPackages = snapshot.AppPackages.Select(static package => new SoftwareProtocolEntry(
            package.Id,
            package.Name,
            package.Version,
            package.Publisher,
            package.Architecture,
            null,
            null,
            "appx",
            package.UserId,
            null,
            null,
            package.IsFramework,
            package.IsFramework ? "Framework" : "AppX"));
        IEnumerable<SoftwareProtocolEntry> hotfixes = snapshot.Hotfixes.Select(static hotfix => new SoftwareProtocolEntry(
            hotfix.Id,
            hotfix.Id,
            null,
            null,
            null,
            hotfix.InstalledAt,
            null,
            "hotfix",
            null,
            null,
            null,
            true,
            hotfix.Classification));

        return registry.Concat(appPackages).Concat(hotfixes)
            .OrderBy(static item => SourcePriority(item.Source))
            .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(
                static item => InventoryNormalizer.StableKey(item.Name, item.Version, item.Architecture, item.UserId),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Architecture, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.UserId, StringComparer.OrdinalIgnoreCase)
            .Select(static software => Record(
                ("arch", software.Architecture),
                ("comments", software.ReleaseType),
                ("filesize", ToInt64(software.SizeBytes)),
                ("from", software.Source),
                ("guid", software.Id),
                ("install_date", FormatDateTime(software.InstallDate)),
                ("name", software.Name),
                ("no_remove", software.IsSystemComponent),
                ("publisher", software.Publisher),
                ("release_type", software.ReleaseType),
                ("uninstall_string", software.UninstallCommand),
                ("url_info_about", software.Url),
                ("userid", software.UserId),
                ("version", software.Version)));
    }

    private static SortedDictionary<string, object?> MapUsb(UsbDeviceInfo usb)
    {
        return Record(
            ("caption", usb.Name),
            ("class", usb.Class),
            ("name", usb.Name),
            ("productid", usb.ProductId),
            ("serial", usb.SerialNumber),
            ("vendorid", usb.VendorId));
    }

    private static SortedDictionary<string, object?> MapLocalUser(UserInfo user)
    {
        return Record(("id", user.Id), ("login", user.Name), ("name", user.FullName));
    }

    private static SortedDictionary<string, object?> MapLocalGroup(GroupInfo group)
    {
        return Record(("id", group.Id), ("members", group.MemberIds), ("name", group.Name));
    }

    private static SortedDictionary<string, object?> MapSession(SessionInfo session)
    {
        return Record(("domain", session.Domain), ("login", session.UserName));
    }

    private static SortedDictionary<string, object?> MapProcess(ProcessInfo process)
    {
        return Record(
            ("cmd", process.CommandLine ?? process.Name),
            ("pid", process.ProcessId),
            ("started", FormatDateTime(process.StartedAt)),
            ("user", process.Owner ?? "unknown"),
            ("virtualmemory", ToKibibytes(process.WorkingSetBytes)));
    }

    private static SortedDictionary<string, object?> MapPrinter(PrinterInfo printer)
    {
        return Record(
            ("description", printer.IsDefault ? "Default printer" : null),
            ("driver", printer.Driver),
            ("name", printer.Name),
            ("network", printer.IsNetwork),
            ("port", printer.Port),
            ("shared", printer.IsShared),
            ("status", printer.Status));
    }

    private static SortedDictionary<string, object?> MapMonitor(MonitorInfo monitor)
    {
        return Record(
            ("caption", monitor.Model),
            ("description", monitor.ManufactureDate?.ToString("MM/yyyy", CultureInfo.InvariantCulture)),
            ("manufacturer", monitor.Manufacturer),
            ("name", monitor.Model),
            ("serial", monitor.SerialNumber));
    }

    private static SortedDictionary<string, object?> MapController(ControllerInfo controller)
    {
        return Record(
            ("caption", controller.Name),
            ("manufacturer", controller.Manufacturer),
            ("name", controller.Name),
            ("serial", controller.Id),
            ("type", controller.Type));
    }

    private static SortedDictionary<string, object?> MapVideo(VideoAdapterInfo video)
    {
        return Record(
            ("chipset", video.Manufacturer),
            ("memory", ToMebibytes(video.MemoryBytes)),
            ("name", video.Name),
            ("resolution", video.Resolution));
    }

    private static SortedDictionary<string, object?> MapBattery(BatteryInfo battery)
    {
        return Record(
            ("capacity", ToInt64(battery.DesignCapacityMilliwattHours)),
            ("chemistry", battery.Chemistry),
            ("manufacturer", battery.Manufacturer),
            ("name", battery.Name ?? battery.Id),
            ("real_capacity", ToInt64(battery.FullChargeCapacityMilliwattHours)),
            ("serial", battery.Id),
            ("voltage", battery.VoltageMillivolts));
    }

    private static SortedDictionary<string, object?> MapSound(SoundDeviceInfo sound)
    {
        return Record(
            ("caption", sound.Name),
            ("description", sound.Status),
            ("manufacturer", sound.Manufacturer),
            ("name", sound.Name));
    }

    private static SortedDictionary<string, object?> MapInput(InputDeviceInfo input)
    {
        return Record(("caption", input.Name), ("description", input.Status), ("name", input.Name), ("type", input.Type));
    }

    private static SortedDictionary<string, object?> MapPort(PortInfo port)
    {
        return Record(("caption", port.Name), ("description", port.Description), ("name", port.Name), ("type", port.Type ?? "Other"));
    }

    private static SortedDictionary<string, object?> MapModem(PortInfo modem)
    {
        return Record(("description", modem.Description), ("name", modem.Name), ("serial", modem.Id), ("type", modem.Type));
    }

    private static SortedDictionary<string, object?> MapAntivirus(AntivirusInfo antivirus)
    {
        return Record(
            ("company", antivirus.Publisher),
            ("enabled", antivirus.Enabled),
            ("guid", antivirus.Id),
            ("name", antivirus.Name),
            ("uptodate", antivirus.UpToDate),
            ("version", antivirus.Version));
    }

    private static SortedDictionary<string, object?> MapFirewall(FirewallProfileInfo firewall)
    {
        return Record(
            ("description", $"Inbound={firewall.DefaultInboundAction}; Outbound={firewall.DefaultOutboundAction}"),
            ("profile", firewall.Name),
            ("status", firewall.Enabled ? "on" : "off"));
    }

    private static SortedDictionary<string, object?> Record(params (string Key, object? Value)[] values)
    {
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, object? value) in values)
        {
            if (value is null || value is string text && string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static void Add(SortedDictionary<string, object> content, string name, object? value)
    {
        if (value is not null)
        {
            content[name] = value;
        }
    }

    private static void AddList(
        SortedDictionary<string, object> content,
        string name,
        IEnumerable<SortedDictionary<string, object?>> values)
    {
        SortedDictionary<string, object?>[] items = values.Where(static item => item.Count > 0).ToArray();
        if (items.Length > 0)
        {
            content[name] = items;
        }
    }

    private static long? ToMebibytes(ulong? bytes) => bytes is null ? null : ToInt64(bytes.Value / 1_048_576UL);

    private static long? ToKibibytes(ulong? bytes) => bytes is null ? null : ToInt64(bytes.Value / 1024UL);

    private static long? ToInt64(ulong? value) => value is null || value > long.MaxValue ? null : (long)value.Value;

    private static string? FormatDate(DateTimeOffset? value) => value?.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? FormatDateTime(DateTimeOffset? value) => value?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? DriveLetter(string? value)
    {
        return value?.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':' ? value[..2].ToUpperInvariant() : null;
    }

    private static string FullyQualifiedName(OperatingSystemInfo operatingSystem)
    {
        return string.IsNullOrWhiteSpace(operatingSystem.Domain)
            ? operatingSystem.Hostname
            : $"{operatingSystem.Hostname}.{operatingSystem.Domain}";
    }

    private static string? DetectVirtualization(HardwareInfo? hardware)
    {
        string identity = $"{hardware?.Manufacturer} {hardware?.Model}";
        return identity.Contains("virtual", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("vmware", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("hyper-v", StringComparison.OrdinalIgnoreCase)
            ? "Virtual Machine"
            : hardware is null ? null : "Physical";
    }

    private static string MapNetworkStatus(string? status)
    {
        return status?.ToUpperInvariant() switch
        {
            "UP" or "CONNECTED" => "up",
            "DOWN" or "DISCONNECTED" => "down",
            "DORMANT" => "dormant",
            "NOTPRESENT" => "notpresent",
            "TESTING" => "testing",
            _ => "unknown",
        };
    }

    private static string MapNetworkType(string? type)
    {
        string normalized = type?.ToUpperInvariant() ?? string.Empty;
        if (normalized.Contains("WIRELESS", StringComparison.Ordinal) || normalized.Contains("WIFI", StringComparison.Ordinal))
        {
            return "wifi";
        }

        if (normalized.Contains("LOOPBACK", StringComparison.Ordinal))
        {
            return "loopback";
        }

        if (normalized.Contains("BLUETOOTH", StringComparison.Ordinal))
        {
            return "bluetooth";
        }

        if (normalized.Contains("DIAL", StringComparison.Ordinal) || normalized.Contains("PPP", StringComparison.Ordinal))
        {
            return "dialup";
        }

        return "ethernet";
    }

    private static (string? Mask, string? Subnet) IPv4Network(string address, int? prefixLength)
    {
        if (prefixLength is null or < 0 or > 32
            || !IPAddress.TryParse(address, out IPAddress? parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            return (null, null);
        }

        uint ip = ToUInt32(parsed.GetAddressBytes());
        uint mask = prefixLength == 0 ? 0U : uint.MaxValue << (32 - prefixLength.Value);
        return (FromUInt32(mask), FromUInt32(ip & mask));
    }

    private static uint ToUInt32(byte[] bytes)
    {
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static string FromUInt32(uint value)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value >> 24}.{(value >> 16) & 0xff}.{(value >> 8) & 0xff}.{value & 0xff}");
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static int SourcePriority(string? source)
    {
        return source switch
        {
            "machine-registry" => 0,
            "user-registry" => 1,
            "offline-user-registry" => 2,
            "appx" => 3,
            "hotfix" => 4,
            _ => 5,
        };
    }

    private sealed record SoftwareProtocolEntry(
        string Id,
        string Name,
        string? Version,
        string? Publisher,
        string? Architecture,
        DateTimeOffset? InstallDate,
        ulong? SizeBytes,
        string? Source,
        string? UserId,
        string? Url,
        string? UninstallCommand,
        bool IsSystemComponent,
        string? ReleaseType);
}
