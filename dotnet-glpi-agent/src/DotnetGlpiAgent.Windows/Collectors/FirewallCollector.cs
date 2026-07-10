using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;
using DotnetGlpiAgent.Windows.Registry;
using Microsoft.Win32;

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

    private static readonly (string Profile, string RegistryKey)[] RegistryProfiles =
    [
        ("Domain", "DomainProfile"),
        ("Private", "StandardProfile"),
        ("Public", "PublicProfile"),
    ];

    private readonly IWmiQueryAdapter _wmi;
    private readonly IRegistryQueryAdapter? _registry;

    public FirewallCollector(
        IWmiQueryAdapter wmi,
        IRegistryQueryAdapter? registry = null,
        IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
        _registry = registry;
    }

    public override string Name => "windows-firewall";

    public override InventoryCategory Category => InventoryCategory.Firewall;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<SourceDiagnostic>();
        (IReadOnlyList<WmiRow> rows, SourceDiagnostic? wmiDiagnostic) = await WmiQueryHelpers.TryQueryAsync(
            _wmi,
            new WmiQuery(
                @"\\.\root\StandardCimv2",
                "MSFT_NetFirewallProfile",
                Properties,
                Timeout: Timeout),
            $"{Name}:MSFT_NetFirewallProfile",
            cancellationToken).ConfigureAwait(false);
        if (wmiDiagnostic is not null)
        {
            diagnostics.Add(wmiDiagnostic);
        }

        FirewallProfileInfo[] profiles = Map(rows);
        if (profiles.Length == 0 && _registry is not null)
        {
            profiles = await ReadRegistryProfilesAsync(cancellationToken).ConfigureAwait(false);
            if (profiles.Length > 0)
            {
                diagnostics.Add(new SourceDiagnostic(
                    Name,
                    CollectionState.Success,
                    "firewall-registry-fallback",
                    "Firewall profiles were read from the SharedAccess registry policy."));
            }
        }

        if (profiles.Length == 0)
        {
            diagnostics.Add(new SourceDiagnostic(
                Name,
                CollectionState.Unavailable,
                "firewall-profiles-not-detected",
                "Windows did not expose firewall profiles."));
        }

        return new InventoryContribution
        {
            Source = Name,
            FirewallProfiles = profiles,
            Diagnostics = diagnostics,
        };
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

    private async ValueTask<FirewallProfileInfo[]> ReadRegistryProfilesAsync(CancellationToken cancellationToken)
    {
        if (_registry is null)
        {
            return [];
        }

        const string root = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
        var profiles = new List<FirewallProfileInfo>(RegistryProfiles.Length);
        foreach ((string profile, string key) in RegistryProfiles)
        {
            RegistryKeySnapshot? snapshot = await _registry.ReadKeyAsync(
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                $"{root}\\{key}",
                ["EnableFirewall"],
                cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                continue;
            }

            bool enabled = snapshot.GetBoolean("EnableFirewall") == true
                || snapshot.GetUInt64("EnableFirewall") is > 0;
            profiles.Add(new FirewallProfileInfo(
                profile.ToLowerInvariant(),
                profile,
                enabled,
                null,
                null));
        }

        return profiles
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
