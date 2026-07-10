namespace DotnetGlpiAgent.Core.Inventory;

/// <summary>
/// Merges collector contributions into one stable inventory snapshot.
/// </summary>
public static class InventorySnapshotAssembler
{
    public static InventorySnapshot Assemble(
        AgentIdentity identity,
        InventoryAccount account,
        CollectionMetadata metadata,
        IEnumerable<InventoryContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(contributions);

        InventoryContribution[] ordered = contributions
            .OrderBy(static contribution => contribution.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static contribution => contribution.Source, StringComparer.Ordinal)
            .ToArray();

        return new InventorySnapshot
        {
            Identity = identity,
            Account = account,
            Collection = metadata,
            OperatingSystem = ordered.Select(static item => item.OperatingSystem).FirstOrDefault(static item => item is not null),
            Hardware = ordered.Select(static item => item.Hardware).FirstOrDefault(static item => item is not null),
            Bios = ordered.Select(static item => item.Bios).FirstOrDefault(static item => item is not null),
            Cpus = Merge(ordered.SelectMany(static item => item.Cpus), static item => item.Id),
            MemoryModules = Merge(ordered.SelectMany(static item => item.MemoryModules), static item => item.Id),
            StorageDevices = Merge(ordered.SelectMany(static item => item.StorageDevices), static item => item.Id),
            Volumes = Merge(ordered.SelectMany(static item => item.Volumes), static item => item.Id),
            NetworkAdapters = Merge(ordered.SelectMany(static item => item.NetworkAdapters), static item => item.Id),
            UsbDevices = Merge(ordered.SelectMany(static item => item.UsbDevices), static item => item.Id),
            PnpDevices = Merge(ordered.SelectMany(static item => item.PnpDevices), static item => item.Id),
            Software = Merge(ordered.SelectMany(static item => item.Software), SoftwareKey),
            Users = Merge(ordered.SelectMany(static item => item.Users), static item => item.Id),
            Groups = Merge(ordered.SelectMany(static item => item.Groups), static item => item.Id),
            Sessions = Merge(ordered.SelectMany(static item => item.Sessions), static item => item.Id),
            Processes = Merge(ordered.SelectMany(static item => item.Processes), static item => item.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Hotfixes = Merge(ordered.SelectMany(static item => item.Hotfixes), static item => item.Id),
            AppPackages = Merge(ordered.SelectMany(static item => item.AppPackages), AppPackageKey),
            Printers = Merge(ordered.SelectMany(static item => item.Printers), static item => item.Id),
            Monitors = Merge(ordered.SelectMany(static item => item.Monitors), static item => item.Id),
            Controllers = Merge(ordered.SelectMany(static item => item.Controllers), static item => item.Id),
            VideoAdapters = Merge(ordered.SelectMany(static item => item.VideoAdapters), static item => item.Id),
            Batteries = Merge(ordered.SelectMany(static item => item.Batteries), static item => item.Id),
            SoundDevices = Merge(ordered.SelectMany(static item => item.SoundDevices), static item => item.Id),
            InputDevices = Merge(ordered.SelectMany(static item => item.InputDevices), static item => item.Id),
            Ports = Merge(ordered.SelectMany(static item => item.Ports), static item => item.Id),
            AntivirusProducts = Merge(ordered.SelectMany(static item => item.AntivirusProducts), static item => item.Id),
            FirewallProfiles = Merge(ordered.SelectMany(static item => item.FirewallProfiles), static item => item.Id),
        };
    }

    private static T[] Merge<T>(IEnumerable<T> items, Func<T, string> keySelector)
    {
        return items
            .DistinctBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .OrderBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ThenBy(keySelector, StringComparer.Ordinal)
            .ToArray();
    }

    private static string SoftwareKey(SoftwareInfo software)
    {
        return string.Join(
            '\u001f',
            software.Name,
            software.Version ?? string.Empty,
            software.Architecture ?? string.Empty,
            software.UserId ?? string.Empty);
    }

    private static string AppPackageKey(AppPackageInfo package)
    {
        return string.Join(
            '\u001f',
            package.Name,
            package.Version ?? string.Empty,
            package.Architecture ?? string.Empty,
            package.UserId ?? string.Empty);
    }
}
