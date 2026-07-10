using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.AppPackages;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class AppPackageCollector : WindowsCollectorBase
{
    private readonly IAppPackageDataAdapter _packages;

    public AppPackageCollector(IAppPackageDataAdapter packages, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _packages = packages;
    }

    public override string Name => "windows-appx-packages";

    public override InventoryCategory Category => InventoryCategory.AppPackage;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        AppPackageEnumerationResult result = await _packages.EnumerateAsync(cancellationToken).ConfigureAwait(false);
        return new InventoryContribution
        {
            Source = Name,
            AppPackages = Map(result.Packages),
            Diagnostics = result.Diagnostics,
        };
    }

    public static AppPackageInfo[] Map(IEnumerable<AppPackageDataSnapshot> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return packages
            .Select(MapPackage)
            .Where(static package => package is not null)
            .Select(static package => package!)
            .OrderBy(static package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Architecture, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.UserId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(
                static package => InventoryNormalizer.StableKey(
                    package.Name,
                    package.Version,
                    package.Architecture,
                    package.UserId),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AppPackageInfo? MapPackage(AppPackageDataSnapshot package)
    {
        string? name = InventoryNormalizer.CleanString(package.Name);
        string? id = InventoryNormalizer.CleanString(package.Id);
        string? sid = InventoryNormalizer.CleanString(package.UserSid);
        if (name is null || id is null || sid is null)
        {
            return null;
        }

        return new AppPackageInfo(
            id,
            name,
            InventoryNormalizer.CleanString(package.Version),
            InventoryNormalizer.CleanIdentity(package.Publisher),
            InventoryNormalizer.NormalizeArchitecture(package.Architecture),
            sid,
            package.IsFramework);
    }
}
