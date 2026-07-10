using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;

namespace DotnetGlpiAgent.Windows.Collectors;

public interface IWindowsPlatform
{
    bool IsWindows { get; }
}

public sealed class WindowsPlatform : IWindowsPlatform
{
    public bool IsWindows => OperatingSystem.IsWindows();
}

public abstract class WindowsCollectorBase : IInventoryCollector
{
    private readonly IWindowsPlatform _platform;

    protected WindowsCollectorBase(IWindowsPlatform? platform = null)
    {
        _platform = platform ?? new WindowsPlatform();
    }

    public abstract string Name { get; }

    public abstract InventoryCategory Category { get; }

    public virtual TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public ValueTask<CollectorSupport> GetSupportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_platform.IsWindows
            ? CollectorSupport.Supported
            : new CollectorSupport(CollectorSupportState.Unavailable, "windows-only"));
    }

    public abstract ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken);
}
