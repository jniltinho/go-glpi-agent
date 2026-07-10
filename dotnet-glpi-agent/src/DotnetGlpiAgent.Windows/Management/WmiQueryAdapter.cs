using System.Management;

using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;

namespace DotnetGlpiAgent.Windows.Management;

public interface IWmiQueryAdapter
{
    ValueTask<IReadOnlyList<WmiRow>> QueryAsync(WmiQuery query, CancellationToken cancellationToken);
}

public sealed class WmiQueryAdapter : IWmiQueryAdapter
{
    public async ValueTask<IReadOnlyList<WmiRow>> QueryAsync(
        WmiQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await Task.Run(
                () => QueryCore(query, cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CollectorFailureException(CollectionState.AccessDenied, "wmi-access-denied", exception.Message);
        }
        catch (ManagementException exception) when (exception.ErrorCode == ManagementStatus.AccessDenied)
        {
            throw new CollectorFailureException(CollectionState.AccessDenied, "wmi-access-denied", exception.Message);
        }
        catch (ManagementException exception) when (exception.ErrorCode == ManagementStatus.Timedout)
        {
            throw new OperationCanceledException("The native WMI query timed out.", exception, cancellationToken);
        }
        catch (ManagementException exception) when (exception.ErrorCode is
            ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass or ManagementStatus.NotFound)
        {
            throw new CollectorFailureException(CollectionState.Unavailable, "wmi-source-unavailable", exception.Message);
        }
        catch (ManagementException exception)
        {
            throw new CollectorFailureException(CollectionState.Failed, "wmi-query-failed", exception.Message);
        }
    }

    private static List<WmiRow> QueryCore(WmiQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TimeSpan timeout = query.Timeout.GetValueOrDefault(TimeSpan.FromSeconds(30));
        var scope = new ManagementScope(query.NamespacePath);
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery(query.ToWql()),
            new System.Management.EnumerationOptions
            {
                BlockSize = 32,
                DirectRead = true,
                EnsureLocatable = false,
                EnumerateDeep = false,
                ReturnImmediately = false,
                Rewindable = false,
                Timeout = timeout,
            });
        using ManagementObjectCollection objects = searcher.Get();
        var rows = new List<WmiRow>();

        foreach (ManagementBaseObject instance in objects)
        {
            using (instance)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (string property in query.Properties)
                {
                    values[property] = CloneValue(instance[property]);
                }

                rows.Add(new WmiRow(values));
            }
        }

        return rows;
    }

    private static object? CloneValue(object? value)
    {
        return value is Array array ? array.Clone() : value;
    }
}
