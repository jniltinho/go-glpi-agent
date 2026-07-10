using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class FirewallCollector : WindowsCollectorBase
{
    private static readonly string[] Properties =
    [
        "DefaultInboundAction",
        "DefaultOutboundAction",
        "Enabled",
        "Name",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public FirewallCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-firewall";

    public override InventoryCategory Category => InventoryCategory.Firewall;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<WmiRow> rows = await _wmi.QueryAsync(
                new WmiQuery(
                    @"\\.\root\StandardCimv2",
                    "MSFT_NetFirewallProfile",
                    Properties,
                    Timeout: Timeout),
                cancellationToken).ConfigureAwait(false);
            FirewallProfileInfo[] profiles = Map(rows);
            return new InventoryContribution
            {
                Source = Name,
                FirewallProfiles = profiles,
                Diagnostics = profiles.Length == 0
                    ? [new SourceDiagnostic(Name, CollectionState.Unavailable, "firewall-profiles-not-detected", "Windows did not expose firewall profiles.")]
                    : [],
            };
        }
        catch (CollectorFailureException exception)
            when (exception.State is CollectionState.Unavailable or CollectionState.AccessDenied)
        {
            return new InventoryContribution
            {
                Source = Name,
                Diagnostics = [new SourceDiagnostic(Name, exception.State, exception.DiagnosticCode, exception.Message)],
            };
        }
    }

    public static FirewallProfileInfo[] Map(IEnumerable<WmiRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Select(MapProfile)
            .Where(static item => item is not null)
            .Select(static item => item!)
            .DistinctBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FirewallProfileInfo? MapProfile(WmiRow row)
    {
        string? name = InventoryNormalizer.CleanString(row.GetString("Name"));
        if (name is null)
        {
            return null;
        }

        return new FirewallProfileInfo(
            name.ToLowerInvariant(),
            name,
            row.GetBoolean("Enabled") == true,
            MapAction(row.GetUInt32("DefaultInboundAction"), row.GetString("DefaultInboundAction")),
            MapAction(row.GetUInt32("DefaultOutboundAction"), row.GetString("DefaultOutboundAction")));
    }

    private static string? MapAction(uint? value, string? text)
    {
        return value switch
        {
            0 => "NotConfigured",
            1 => "Allow",
            2 => "Block",
            _ => InventoryNormalizer.CleanString(text),
        };
    }
}
