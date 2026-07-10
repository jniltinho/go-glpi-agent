using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Bcl;
using DotnetGlpiAgent.Windows.Management;
using DotnetGlpiAgent.Windows.Registry;
using Microsoft.Win32;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class OperatingSystemCollector : WindowsCollectorBase
{
    private const string CurrentVersionPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private static readonly string[] RegistryValues =
    [
        "CurrentBuildNumber",
        "DisplayVersion",
        "EditionID",
        "ProductName",
        "UBR",
    ];
    private static readonly string[] WmiProperties =
    [
        "BuildNumber",
        "Caption",
        "InstallDate",
        "LastBootUpTime",
        "OSArchitecture",
        "Version",
    ];

    private readonly IWmiQueryAdapter _wmi;
    private readonly IRegistryQueryAdapter _registry;
    private readonly IHostDataAdapter _host;

    public OperatingSystemCollector(
        IWmiQueryAdapter wmi,
        IRegistryQueryAdapter registry,
        IHostDataAdapter host,
        IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
        _registry = registry;
        _host = host;
    }

    public override string Name => "windows-operating-system";

    public override InventoryCategory Category => InventoryCategory.OperatingSystem;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> rows = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_OperatingSystem", WmiProperties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        WmiRow row = rows.Count > 0
            ? rows[0]
            : throw new CollectorFailureException(CollectionState.Unavailable, "operating-system-unavailable", "Win32_OperatingSystem returned no rows.");
        RegistryKeySnapshot? registry = await _registry.ReadKeyAsync(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            CurrentVersionPath,
            RegistryValues,
            cancellationToken).ConfigureAwait(false);
        HostDataSnapshot host = await _host.GetAsync(cancellationToken).ConfigureAwait(false);

        return new InventoryContribution
        {
            Source = Name,
            OperatingSystem = Map(row, registry, host),
        };
    }

    public static OperatingSystemInfo Map(
        WmiRow row,
        RegistryKeySnapshot? registry,
        HostDataSnapshot host)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(host);

        string version = row.GetString("Version") ?? Environment.OSVersion.Version.ToString();
        string build = registry?.GetString("CurrentBuildNumber")
            ?? row.GetString("BuildNumber")
            ?? Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ulong? ubrValue = registry?.GetUInt64("UBR");

        return new OperatingSystemInfo(
            registry?.GetString("ProductName") ?? row.GetString("Caption") ?? host.OperatingSystemDescription,
            registry?.GetString("EditionID"),
            version,
            build,
            registry?.GetString("DisplayVersion"),
            version,
            InventoryNormalizer.NormalizeArchitecture(row.GetString("OSArchitecture") ?? host.OperatingSystemArchitecture) ?? "unknown",
            host.MachineName,
            host.DomainName,
            row.GetDateTime("LastBootUpTime") ?? host.BootTimeUtc,
            row.GetDateTime("InstallDate"),
            host.TimeZoneId,
            ubrValue is <= int.MaxValue ? (int?)ubrValue : null);
    }
}
