using System.Net;

using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Bcl;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class NetworkCollector : WindowsCollectorBase
{
    private static readonly string[] AdapterProperties =
    [
        "AdapterType",
        "Description",
        "GUID",
        "InterfaceIndex",
        "MACAddress",
        "Name",
        "NetConnectionStatus",
        "PhysicalAdapter",
        "PNPDeviceID",
        "Speed",
    ];
    private static readonly string[] ConfigurationProperties =
    [
        "DefaultIPGateway",
        "Description",
        "DHCPEnabled",
        "DNSServerSearchOrder",
        "InterfaceIndex",
        "IPAddress",
        "IPSubnet",
        "MACAddress",
        "SettingID",
    ];

    private readonly INetworkDataAdapter _network;
    private readonly IWmiQueryAdapter _wmi;

    public NetworkCollector(
        INetworkDataAdapter network,
        IWmiQueryAdapter wmi,
        IWindowsPlatform? platform = null)
        : base(platform)
    {
        _network = network;
        _wmi = wmi;
    }

    public override string Name => "windows-network";

    public override InventoryCategory Category => InventoryCategory.Network;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NetworkDataSnapshot> bcl = await _network.GetAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> adapters = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_NetworkAdapter", AdapterProperties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> configurations = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_NetworkAdapterConfiguration", ConfigurationProperties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);

        return new InventoryContribution
        {
            Source = Name,
            NetworkAdapters = Map(bcl, adapters, configurations),
        };
    }

    public static NetworkAdapterInfo[] Map(
        IEnumerable<NetworkDataSnapshot> bclAdapters,
        IEnumerable<WmiRow> wmiAdapters,
        IEnumerable<WmiRow> wmiConfigurations)
    {
        ArgumentNullException.ThrowIfNull(bclAdapters);
        ArgumentNullException.ThrowIfNull(wmiAdapters);
        ArgumentNullException.ThrowIfNull(wmiConfigurations);

        WmiRow[] adapters = wmiAdapters.ToArray();
        WmiRow[] configurations = wmiConfigurations.ToArray();
        var result = new List<NetworkAdapterInfo>();
        foreach (NetworkDataSnapshot bcl in bclAdapters)
        {
            WmiRow? adapter = FindMatch(adapters, bcl);
            WmiRow? configuration = FindMatch(configurations, bcl);
            string baseId = InventoryNormalizer.NormalizeIdentifier(adapter?.GetString("GUID") ?? bcl.Id) ?? bcl.Id;
            string? macAddress = NormalizeMac(adapter?.GetString("MACAddress")
                ?? configuration?.GetString("MACAddress")
                ?? bcl.MacAddress);
            NetworkAddressInfo[] addresses = MergeAddresses(bcl, configuration);
            string[] gateways = MergeStrings(bcl.Gateways, configuration?.GetStrings("DefaultIPGateway") ?? []);
            string[] dns = MergeStrings(bcl.DnsServers, configuration?.GetStrings("DNSServerSearchOrder") ?? []);
            bool isVirtual = bcl.IsVirtual || IsVirtual(adapter);
            bool? dhcp = configuration?.GetBoolean("DHCPEnabled") ?? bcl.DhcpEnabled;
            ulong? speed = adapter?.GetUInt64("Speed") ?? bcl.SpeedBitsPerSecond;

            if (addresses.Length == 0)
            {
                result.Add(Create(baseId, bcl, adapter, macAddress, speed, isVirtual, dhcp, [], gateways, dns));
                continue;
            }

            foreach (NetworkAddressInfo address in addresses)
            {
                result.Add(Create(
                    $"{baseId}#{address.Address}",
                    bcl,
                    adapter,
                    macAddress,
                    speed,
                    isVirtual,
                    dhcp,
                    [address],
                    gateways,
                    dns));
            }
        }

        return result
            .DistinctBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static NetworkAdapterInfo Create(
        string id,
        NetworkDataSnapshot bcl,
        WmiRow? adapter,
        string? macAddress,
        ulong? speed,
        bool isVirtual,
        bool? dhcp,
        IReadOnlyList<NetworkAddressInfo> addresses,
        IReadOnlyList<string> gateways,
        IReadOnlyList<string> dns)
    {
        return new NetworkAdapterInfo(
            id,
            InventoryNormalizer.CleanString(adapter?.GetString("Name") ?? bcl.Name),
            InventoryNormalizer.CleanString(adapter?.GetString("Description") ?? bcl.Description),
            macAddress,
            InventoryNormalizer.CleanString(adapter?.GetString("AdapterType") ?? bcl.InterfaceType),
            MapConnectionStatus(adapter?.GetUInt32("NetConnectionStatus")) ?? bcl.Status,
            speed,
            isVirtual,
            dhcp,
            addresses,
            gateways,
            dns);
    }

    private static WmiRow? FindMatch(IEnumerable<WmiRow> rows, NetworkDataSnapshot bcl)
    {
        string? normalizedBclMac = NormalizeMac(bcl.MacAddress);
        return rows.FirstOrDefault(row =>
            string.Equals(
                InventoryNormalizer.NormalizeIdentifier(row.GetString("GUID") ?? row.GetString("SettingID")),
                InventoryNormalizer.NormalizeIdentifier(bcl.Id),
                StringComparison.OrdinalIgnoreCase)
            || (normalizedBclMac is not null
                && string.Equals(NormalizeMac(row.GetString("MACAddress")), normalizedBclMac, StringComparison.OrdinalIgnoreCase)));
    }

    private static NetworkAddressInfo[] MergeAddresses(
        NetworkDataSnapshot bcl,
        WmiRow? configuration)
    {
        var addresses = bcl.Addresses.Select(static item => new NetworkAddressInfo(
            item.Address,
            item.PrefixLength,
            item.Family,
            null)).ToList();
        IReadOnlyList<string> wmiAddresses = configuration?.GetStrings("IPAddress") ?? [];
        IReadOnlyList<string> subnets = configuration?.GetStrings("IPSubnet") ?? [];
        for (int index = 0; index < wmiAddresses.Count; index++)
        {
            string address = wmiAddresses[index];
            addresses.Add(new NetworkAddressInfo(
                address,
                index < subnets.Count ? PrefixLength(subnets[index]) : null,
                IPAddress.TryParse(address, out IPAddress? parsed) ? parsed.AddressFamily.ToString() : null,
                configuration?.GetBoolean("DHCPEnabled")));
        }

        return addresses
            .Where(static item => IPAddress.TryParse(item.Address, out IPAddress? parsed)
                && !IPAddress.IsLoopback(parsed)
                && !parsed.Equals(IPAddress.Any)
                && !parsed.Equals(IPAddress.IPv6Any))
            .DistinctBy(static item => item.Address, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Address, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] MergeStrings(IEnumerable<string> first, IEnumerable<string> second)
    {
        return first.Concat(second)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? NormalizeMac(string? value)
    {
        string? cleaned = InventoryNormalizer.CleanString(value);
        if (cleaned is null)
        {
            return null;
        }

        string hexadecimal = new(cleaned.Where(Uri.IsHexDigit).ToArray());
        return hexadecimal.Length == 12
            ? string.Join(':', Enumerable.Range(0, 6).Select(index => hexadecimal.Substring(index * 2, 2))).ToUpperInvariant()
            : null;
    }

    private static bool IsVirtual(WmiRow? row)
    {
        if (row is null)
        {
            return false;
        }

        if (row.GetBoolean("PhysicalAdapter") == false)
        {
            return true;
        }

        string combined = $"{row.GetString("Name")} {row.GetString("Description")} {row.GetString("PNPDeviceID")}";
        return combined.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("ROOT\\", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("VMWARE", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("VBOX", StringComparison.OrdinalIgnoreCase);
    }

    private static int? PrefixLength(string subnet)
    {
        if (int.TryParse(subnet, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int prefix)
            && prefix is >= 0 and <= 128)
        {
            return prefix;
        }

        if (!IPAddress.TryParse(subnet, out IPAddress? mask))
        {
            return null;
        }

        int bits = 0;
        foreach (byte value in mask.GetAddressBytes())
        {
            bits += System.Numerics.BitOperations.PopCount(value);
        }

        return bits;
    }

    private static string? MapConnectionStatus(uint? value)
    {
        return value switch
        {
            0 => "Disconnected",
            1 => "Connecting",
            2 => "Connected",
            3 => "Disconnecting",
            7 => "Media disconnected",
            null => null,
            _ => $"Status {value}",
        };
    }
}
