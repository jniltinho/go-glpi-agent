using System.Management;

using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;

namespace DotnetGlpiAgent.Windows.Bcl;

public sealed record WindowsProcessDataSnapshot(
    int ProcessId,
    string Name,
    string? Owner,
    string? CommandLine,
    DateTimeOffset? StartedAt,
    ulong? WorkingSetBytes);

public interface IWindowsProcessDataAdapter
{
    ValueTask<IReadOnlyList<WindowsProcessDataSnapshot>> GetAsync(
        int maximumProcesses,
        int maximumCommandLength,
        CancellationToken cancellationToken);
}

public sealed class WindowsProcessDataAdapter : IWindowsProcessDataAdapter
{
    public async ValueTask<IReadOnlyList<WindowsProcessDataSnapshot>> GetAsync(
        int maximumProcesses,
        int maximumCommandLength,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumProcesses, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCommandLength, 1);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await Task.Run(
                () => QueryCore(maximumProcesses, maximumCommandLength, cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CollectorFailureException(CollectionState.AccessDenied, "process-access-denied", exception.Message);
        }
        catch (ManagementException exception)
        {
            throw new CollectorFailureException(CollectionState.Failed, "process-query-failed", exception.Message);
        }
    }

    private static List<WindowsProcessDataSnapshot> QueryCore(
        int maximumProcesses,
        int maximumCommandLength,
        CancellationToken cancellationToken)
    {
        var scope = new ManagementScope(@"\\.\root\cimv2");
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT CommandLine, CreationDate, Name, ProcessId, WorkingSetSize FROM Win32_Process"),
            new System.Management.EnumerationOptions
            {
                ReturnImmediately = false,
                Rewindable = false,
                Timeout = TimeSpan.FromSeconds(30),
            });
        using ManagementObjectCollection objects = searcher.Get();
        var result = new List<WindowsProcessDataSnapshot>();
        foreach (ManagementObject process in objects)
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Count >= maximumProcesses)
                {
                    break;
                }

                uint? processId = ConvertUInt32(process["ProcessId"]);
                if (processId is null or > int.MaxValue)
                {
                    continue;
                }

                string? commandLine = InventoryNormalizer.CleanString(Convert.ToString(process["CommandLine"], System.Globalization.CultureInfo.InvariantCulture));
                result.Add(new WindowsProcessDataSnapshot(
                    (int)processId,
                    InventoryNormalizer.CleanString(Convert.ToString(process["Name"], System.Globalization.CultureInfo.InvariantCulture))
                        ?? $"pid-{processId}",
                    GetOwner(process),
                    commandLine?.Length > maximumCommandLength ? commandLine[..maximumCommandLength] : commandLine,
                    InventoryNormalizer.NormalizeDate(Convert.ToString(process["CreationDate"], System.Globalization.CultureInfo.InvariantCulture)),
                    ConvertUInt64(process["WorkingSetSize"])));
            }
        }

        return result.OrderBy(static process => process.ProcessId).ToList();
    }

    private static string? GetOwner(ManagementObject process)
    {
        try
        {
            object?[] arguments = [null, null];
            object? returnValue = process.InvokeMethod("GetOwner", arguments);
            if (ConvertUInt32(returnValue) != 0)
            {
                return null;
            }

            string? user = InventoryNormalizer.CleanString(arguments[0]?.ToString());
            string? domain = InventoryNormalizer.CleanString(arguments[1]?.ToString());
            return user is null ? null : string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static uint? ConvertUInt32(object? value)
    {
        try
        {
            return value is null ? null : Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static ulong? ConvertUInt64(object? value)
    {
        try
        {
            return value is null ? null : Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
