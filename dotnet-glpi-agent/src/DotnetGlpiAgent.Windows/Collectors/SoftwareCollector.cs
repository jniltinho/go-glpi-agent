using System.Text.RegularExpressions;
using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Registry;
using Microsoft.Win32;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed partial class SoftwareCollector : WindowsCollectorBase
{
    private const string MachineUninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UserUninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UserWow64UninstallPath = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly string[] ValueNames =
    [
        "DisplayName",
        "DisplayVersion",
        "EstimatedSize",
        "InstallDate",
        "Publisher",
        "ReleaseType",
        "SystemComponent",
        "UninstallString",
        "URLInfoAbout",
        "WindowsInstaller",
    ];

    private readonly IRegistryQueryAdapter _registry;
    private readonly IWindowsUserIdentityResolver _users;
    private readonly IOfflineSoftwareScanner? _offlineScanner;

    public SoftwareCollector(
        IRegistryQueryAdapter registry,
        IWindowsUserIdentityResolver users,
        IWindowsPlatform? platform)
        : this(registry, users, null, platform)
    {
    }

    public SoftwareCollector(
        IRegistryQueryAdapter registry,
        IWindowsUserIdentityResolver users,
        IOfflineSoftwareScanner? offlineScanner = null,
        IWindowsPlatform? platform = null)
        : base(platform)
    {
        _registry = registry;
        _users = users;
        _offlineScanner = offlineScanner;
    }

    public override string Name => "windows-software";

    public override InventoryCategory Category => InventoryCategory.Software;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        var software = new List<SoftwareInfo>();
        software.AddRange(MapRows(
            await _registry.EnumerateSubKeysAsync(
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                MachineUninstallPath,
                ValueNames,
                cancellationToken).ConfigureAwait(false),
            "x86_64",
            "machine-registry",
            null,
            null));
        software.AddRange(MapRows(
            await _registry.EnumerateSubKeysAsync(
                RegistryHive.LocalMachine,
                RegistryView.Registry32,
                MachineUninstallPath,
                ValueNames,
                cancellationToken).ConfigureAwait(false),
            "x86",
            "machine-registry",
            null,
            null));

        IReadOnlyList<RegistryKeySnapshot> hives = await _registry.EnumerateSubKeysAsync(
            RegistryHive.Users,
            RegistryView.Default,
            string.Empty,
            [],
            cancellationToken).ConfigureAwait(false);
        foreach (string sid in hives.Select(static hive => LastSegment(hive.Path))
                     .Where(static sid => UserSidRegex().IsMatch(sid))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            string? account = _users.ResolveAccountName(sid);
            software.AddRange(MapRows(
                await _registry.EnumerateSubKeysAsync(
                    RegistryHive.Users,
                    RegistryView.Default,
                    $"{sid}\\{UserUninstallPath}",
                    ValueNames,
                    cancellationToken).ConfigureAwait(false),
                "x86_64",
                "user-registry",
                sid,
                account));
            software.AddRange(MapRows(
                await _registry.EnumerateSubKeysAsync(
                    RegistryHive.Users,
                    RegistryView.Default,
                    $"{sid}\\{UserWow64UninstallPath}",
                    ValueNames,
                    cancellationToken).ConfigureAwait(false),
                "x86",
                "user-registry",
                sid,
                account));
        }

        IReadOnlyList<SourceDiagnostic> diagnostics = [];
        if (context.Options.ScanProfiles)
        {
            if (_offlineScanner is null)
            {
                diagnostics =
                [
                    new SourceDiagnostic(
                        "offline-profiles",
                        CollectionState.Unavailable,
                        "offline-profile-scanner-unavailable",
                        "Offline profile scanning is not configured."),
                ];
            }
            else
            {
                OfflineSoftwareScanResult offline = await _offlineScanner.ScanAsync(cancellationToken).ConfigureAwait(false);
                software.AddRange(offline.Software);
                diagnostics = offline.Diagnostics;
            }
        }

        return new InventoryContribution
        {
            Source = Name,
            Software = Deduplicate(software),
            Diagnostics = diagnostics,
        };
    }

    public static SoftwareInfo[] MapRows(
        IEnumerable<RegistryKeySnapshot> rows,
        string architecture,
        string source,
        string? userSid,
        string? userName)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Select(row => MapRow(row, architecture, source, userSid, userName))
            .Where(static software => software is not null)
            .Select(static software => software!)
            .ToArray();
    }

    public static SoftwareInfo[] Deduplicate(IEnumerable<SoftwareInfo> software)
    {
        ArgumentNullException.ThrowIfNull(software);
        return software
            .OrderBy(static item => SourcePriority(item.Source))
            .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(
                static item => InventoryNormalizer.StableKey(
                    item.Name,
                    item.Version,
                    item.Architecture,
                    item.UserId),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Architecture, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.UserId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SoftwareInfo? MapRow(
        RegistryKeySnapshot row,
        string architecture,
        string source,
        string? userSid,
        string? userName)
    {
        string? name = InventoryNormalizer.CleanString(row.GetString("DisplayName"));
        if (name is null)
        {
            return null;
        }

        string? version = InventoryNormalizer.CleanString(row.GetString("DisplayVersion"));
        bool system = row.GetUInt64("SystemComponent") == 1;
        string? releaseType = InventoryNormalizer.CleanString(row.GetString("ReleaseType"));
        bool update = IsUpdate(name, releaseType);
        string id = InventoryNormalizer.StableKey(
            name,
            version,
            architecture,
            userSid,
            LastSegment(row.Path));

        return new SoftwareInfo(
            id,
            name,
            version,
            InventoryNormalizer.CleanIdentity(row.GetString("Publisher")),
            architecture,
            InventoryNormalizer.NormalizeDate(row.GetString("InstallDate")),
            InventoryNormalizer.BytesFromKibibytes(row.GetUInt64("EstimatedSize")),
            source,
            userSid is null
                ? null
                : userName is null ? userSid : $"{userName} ({userSid})",
            InventoryNormalizer.CleanString(row.GetString("URLInfoAbout")),
            InventoryNormalizer.CleanString(row.GetString("UninstallString")),
            system,
            update);
    }

    private static bool IsUpdate(string name, string? releaseType)
    {
        return releaseType?.Contains("update", StringComparison.OrdinalIgnoreCase) == true
            || releaseType?.Contains("hotfix", StringComparison.OrdinalIgnoreCase) == true
            || name.StartsWith("KB", StringComparison.OrdinalIgnoreCase)
            || name.Contains(" Update ", StringComparison.OrdinalIgnoreCase);
    }

    private static int SourcePriority(string? source)
    {
        return source switch
        {
            "machine-registry" => 0,
            "user-registry" => 1,
            "offline-user-registry" => 2,
            _ => 3,
        };
    }

    private static string LastSegment(string path)
    {
        return path.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault() ?? string.Empty;
    }

    [GeneratedRegex("^S-1-5-(?:21|32)-[0-9-]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex UserSidRegex();
}
