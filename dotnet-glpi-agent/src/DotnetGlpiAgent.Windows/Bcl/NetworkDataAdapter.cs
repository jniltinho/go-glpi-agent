using System.Net.NetworkInformation;

namespace DotnetGlpiAgent.Windows.Bcl;

public sealed record NetworkDataSnapshot(
    string Id,
    string Name,
    string Description,
    string? MacAddress,
    string InterfaceType,
    string Status,
    ulong? SpeedBitsPerSecond,
    bool IsVirtual,
    bool? DhcpEnabled,
    IReadOnlyList<NetworkAddressSnapshot> Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers);

public sealed record NetworkAddressSnapshot(
    string Address,
    int? PrefixLength,
    string Family);

public interface INetworkDataAdapter
{
    ValueTask<IReadOnlyList<NetworkDataSnapshot>> GetAsync(CancellationToken cancellationToken);
}

public sealed class NetworkDataAdapter : INetworkDataAdapter
{
    private const int MaximumAdapters = 1024;

    public ValueTask<IReadOnlyList<NetworkDataSnapshot>> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NetworkDataSnapshot[] adapters = NetworkInterface.GetAllNetworkInterfaces()
            .OrderBy(static adapter => adapter.Id, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumAdapters)
            .Select(adapter => Capture(adapter, cancellationToken))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<NetworkDataSnapshot>>(adapters);
    }

    private static NetworkDataSnapshot Capture(
        NetworkInterface adapter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPInterfaceProperties properties = adapter.GetIPProperties();
        NetworkAddressSnapshot[] addresses = properties.UnicastAddresses
            .Select(static address => new NetworkAddressSnapshot(
                address.Address.ToString(),
                address.PrefixLength,
                address.Address.AddressFamily.ToString()))
            .OrderBy(static address => address.Address, StringComparer.Ordinal)
            .ToArray();
        string[] gateways = properties.GatewayAddresses
            .Select(static gateway => gateway.Address.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] dns = properties.DnsAddresses
            .Select(static address => address.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        string mac = adapter.GetPhysicalAddress().ToString();
        long speed = adapter.Speed;

        return new NetworkDataSnapshot(
            adapter.Id,
            adapter.Name,
            adapter.Description,
            mac.Length == 0 ? null : mac,
            adapter.NetworkInterfaceType.ToString(),
            adapter.OperationalStatus.ToString(),
            speed < 0 ? null : (ulong)speed,
            IsVirtual(adapter),
            GetDhcpEnabled(properties),
            addresses,
            gateways,
            dns);
    }

    private static bool IsVirtual(NetworkInterface adapter)
    {
        if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        {
            return true;
        }

        string value = $"{adapter.Name} {adapter.Description}";
        return value.Contains("virtual", StringComparison.OrdinalIgnoreCase)
            || value.Contains("hyper-v", StringComparison.OrdinalIgnoreCase)
            || value.Contains("vmware", StringComparison.OrdinalIgnoreCase)
            || value.Contains("vbox", StringComparison.OrdinalIgnoreCase)
            || value.Contains("loopback", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? GetDhcpEnabled(IPInterfaceProperties properties)
    {
        try
        {
            return properties.GetIPv4Properties()?.IsDhcpEnabled;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }
}
