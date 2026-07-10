using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class AntivirusCollector : WindowsCollectorBase
{
    private static readonly string[] SecurityCenterProperties =
    [
        "displayName",
        "instanceGuid",
        "pathToSignedProductExe",
        "productState",
        "timestamp",
    ];

    private static readonly string[] DefenderProperties =
    [
        "AMServiceEnabled",
        "AntivirusEnabled",
        "AntivirusSignatureAge",
        "AntivirusSignatureVersion",
        "RealTimeProtectionEnabled",
    ];

    private readonly IWmiQueryAdapter _wmi;

    public AntivirusCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-antivirus";

    public override InventoryCategory Category => InventoryCategory.Antivirus;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<SourceDiagnostic>();
        IReadOnlyList<WmiRow> securityCenter = await TryQueryAsync(
            @"\\.\root\SecurityCenter2",
            "AntiVirusProduct",
            SecurityCenterProperties,
            "security-center",
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> defender = await TryQueryAsync(
            @"\\.\root\Microsoft\Windows\Defender",
            "MSFT_MpComputerStatus",
            DefenderProperties,
            "microsoft-defender",
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        AntivirusInfo[] products = securityCenter.Select(MapSecurityCenter)
            .Where(static item => item is not null)
            .Select(static item => item!)
            .Concat(defender.Select(MapDefender).Where(static item => item is not null).Select(static item => item!))
            .DistinctBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (products.Length == 0 && diagnostics.Count == 0)
        {
            diagnostics.Add(new SourceDiagnostic(
                Name,
                CollectionState.Unavailable,
                "antivirus-not-detected",
                "Windows did not expose an antivirus product."));
        }

        return new InventoryContribution
        {
            Source = Name,
            AntivirusProducts = products,
            Diagnostics = diagnostics,
        };
    }

    public static AntivirusInfo? MapSecurityCenter(WmiRow row)
    {
        string? name = InventoryNormalizer.CleanString(row.GetString("displayName"));
        if (name is null)
        {
            return null;
        }

        uint? state = row.GetUInt32("productState");
        string id = InventoryNormalizer.NormalizeIdentifier(row.GetString("instanceGuid"))
            ?? InventoryNormalizer.StableKey(name, "security-center");
        return new AntivirusInfo(
            id,
            name,
            null,
            null,
            state is null ? null : (state.Value & 0x1000U) != 0,
            state is null ? null : (state.Value & 0x10U) == 0,
            "SecurityCenter2");
    }

    public static AntivirusInfo? MapDefender(WmiRow row)
    {
        bool? antivirusEnabled = row.GetBoolean("AntivirusEnabled");
        bool? serviceEnabled = row.GetBoolean("AMServiceEnabled");
        bool? realTime = row.GetBoolean("RealTimeProtectionEnabled");
        if (antivirusEnabled is null && serviceEnabled is null && realTime is null)
        {
            return null;
        }

        uint? signatureAge = row.GetUInt32("AntivirusSignatureAge");
        return new AntivirusInfo(
            "microsoft-defender",
            "Microsoft Defender Antivirus",
            InventoryNormalizer.CleanString(row.GetString("AntivirusSignatureVersion")),
            "Microsoft Corporation",
            antivirusEnabled == true && serviceEnabled != false && realTime != false,
            signatureAge is null ? null : signatureAge <= 1,
            "Microsoft Defender");
    }

    private async ValueTask<IReadOnlyList<WmiRow>> TryQueryAsync(
        string namespacePath,
        string className,
        IReadOnlyList<string> properties,
        string diagnosticSource,
        List<SourceDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _wmi.QueryAsync(
                new WmiQuery(namespacePath, className, properties, Timeout: Timeout),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CollectorFailureException exception)
            when (exception.State is CollectionState.Unavailable or CollectionState.AccessDenied)
        {
            diagnostics.Add(new SourceDiagnostic(
                diagnosticSource,
                exception.State,
                exception.DiagnosticCode,
                exception.Message));
            return [];
        }
    }
}
