using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;

namespace DotnetGlpiAgent.Windows.Management;

/// <summary>
/// Best-effort WMI queries so multi-source collectors can degrade per class
/// without aborting the whole category (Perl/official agent behavior).
/// </summary>
public static class WmiQueryHelpers
{
    public static async ValueTask<(IReadOnlyList<WmiRow> Rows, SourceDiagnostic? Diagnostic)> TryQueryAsync(
        IWmiQueryAdapter wmi,
        WmiQuery query,
        string sourceLabel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wmi);
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            IReadOnlyList<WmiRow> rows = await wmi.QueryAsync(query, cancellationToken).ConfigureAwait(false);
            return (rows, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CollectorFailureException exception)
            when (exception.State is CollectionState.Unavailable or CollectionState.AccessDenied)
        {
            return (
                Array.Empty<WmiRow>(),
                new SourceDiagnostic(
                    sourceLabel,
                    exception.State,
                    exception.DiagnosticCode,
                    exception.Message));
        }
        catch (CollectorFailureException exception)
        {
            return (
                Array.Empty<WmiRow>(),
                new SourceDiagnostic(
                    sourceLabel,
                    CollectionState.Failed,
                    exception.DiagnosticCode,
                    exception.Message));
        }
        catch (Exception exception)
        {
            return (
                Array.Empty<WmiRow>(),
                new SourceDiagnostic(
                    sourceLabel,
                    CollectionState.Failed,
                    "wmi-query-failed",
                    exception.GetType().Name));
        }
    }
}
