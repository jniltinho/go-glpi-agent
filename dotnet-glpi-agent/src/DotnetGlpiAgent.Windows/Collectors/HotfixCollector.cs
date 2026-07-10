using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class HotfixCollector : WindowsCollectorBase
{
    private static readonly string[] Properties =
    [
        "Caption",
        "Description",
        "HotFixID",
        "InstalledBy",
        "InstalledOn",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public HotfixCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-hotfixes";

    public override InventoryCategory Category => InventoryCategory.Hotfix;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> rows = await _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", "Win32_QuickFixEngineering", Properties, Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);
        return new InventoryContribution
        {
            Source = Name,
            Hotfixes = rows.Select(Map)
                .Where(static hotfix => hotfix is not null)
                .Select(static hotfix => hotfix!)
                .DistinctBy(static hotfix => hotfix.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static hotfix => hotfix.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    public static HotfixInfo? Map(WmiRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string? id = InventoryNormalizer.CleanString(row.GetString("HotFixID"));
        if (id is null)
        {
            return null;
        }

        string? description = InventoryNormalizer.CleanString(
            row.GetString("Description") ?? row.GetString("Caption"));
        return new HotfixInfo(
            id,
            description,
            InventoryNormalizer.CleanString(row.GetString("InstalledBy")),
            InventoryNormalizer.NormalizeDate(row.GetString("InstalledOn")),
            Classify(description));
    }

    private static string Classify(string? description)
    {
        if (description?.Contains("security", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Security Update";
        }

        if (description?.Contains("hotfix", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Hotfix";
        }

        return "Update";
    }
}
