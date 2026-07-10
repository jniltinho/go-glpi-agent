using System.Diagnostics;

namespace DotnetGlpiAgent.Windows.Bcl;

public sealed record ProcessDataSnapshot(
    int ProcessId,
    string Name,
    DateTimeOffset? StartedAt,
    ulong? WorkingSetBytes);

public interface IProcessDataAdapter
{
    ValueTask<IReadOnlyList<ProcessDataSnapshot>> GetAsync(
        int maximumProcesses,
        CancellationToken cancellationToken);
}

public sealed class ProcessDataAdapter : IProcessDataAdapter
{
    public ValueTask<IReadOnlyList<ProcessDataSnapshot>> GetAsync(
        int maximumProcesses,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumProcesses, 1);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = new List<ProcessDataSnapshot>();
        Process[] processes = Process.GetProcesses();
        try
        {
            foreach (Process process in processes.OrderBy(static process => process.Id).Take(maximumProcesses))
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshots.Add(new ProcessDataSnapshot(
                    process.Id,
                    SafeGet(() => process.ProcessName) ?? $"pid-{process.Id}",
                    SafeGet(() => new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero)),
                    ToUnsigned(SafeGet(() => process.WorkingSet64))));
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        return ValueTask.FromResult<IReadOnlyList<ProcessDataSnapshot>>(snapshots);
    }

    private static T? SafeGet<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return default;
        }
    }

    private static ulong? ToUnsigned(long? value) => value is >= 0 ? (ulong)value.Value : null;
}
