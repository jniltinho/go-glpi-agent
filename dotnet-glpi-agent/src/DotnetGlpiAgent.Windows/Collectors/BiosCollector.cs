using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class BiosCollector : WindowsCollectorBase
{
    private static readonly string[] Properties =
    [
        "Manufacturer",
        "Name",
        "ReleaseDate",
        "SerialNumber",
        "SMBIOSBIOSVersion",
        "SMBIOSMajorVersion",
        "SMBIOSMinorVersion",
        "Version",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public BiosCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-bios";

    public override InventoryCategory Category => InventoryCategory.Bios;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> rows = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_BIOS", Properties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        WmiRow row = rows.Count > 0
            ? rows[0]
            : throw new CollectorFailureException(CollectionState.Unavailable, "bios-unavailable", "Win32_BIOS returned no rows.");

        return new InventoryContribution { Source = Name, Bios = Map(row) };
    }

    public static BiosInfo Map(WmiRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        uint? major = row.GetUInt32("SMBIOSMajorVersion");
        uint? minor = row.GetUInt32("SMBIOSMinorVersion");
        string? smbios = major is null ? null : $"{major}.{minor.GetValueOrDefault()}";

        return new BiosInfo(
            InventoryNormalizer.CleanIdentity(row.GetString("Manufacturer")),
            InventoryNormalizer.CleanIdentity(row.GetString("Name")),
            InventoryNormalizer.CleanIdentity(row.GetString("SMBIOSBIOSVersion") ?? row.GetString("Version")),
            InventoryNormalizer.CleanIdentity(row.GetString("SerialNumber")),
            row.GetDateTime("ReleaseDate"),
            smbios,
            null);
    }
}
