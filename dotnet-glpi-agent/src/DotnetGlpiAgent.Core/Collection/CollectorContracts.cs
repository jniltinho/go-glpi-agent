using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;

namespace DotnetGlpiAgent.Core.Collection;

public interface IInventoryCollector
{
    string Name { get; }

    InventoryCategory Category { get; }

    TimeSpan Timeout { get; }

    ValueTask<CollectorSupport> GetSupportAsync(CancellationToken cancellationToken);

    ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken);
}

public sealed record CollectorContext(
    AgentOptions Options,
    DateTimeOffset DeadlineUtc,
    string CorrelationId);

public enum CollectorSupportState
{
    Supported,
    Unavailable,
    Disabled,
}

public sealed record CollectorSupport(
    CollectorSupportState State,
    string? DiagnosticCode = null)
{
    public static CollectorSupport Supported { get; } = new(CollectorSupportState.Supported);
}

public enum InventoryCategory
{
    OperatingSystem,
    Hardware,
    Bios,
    Cpu,
    Memory,
    Storage,
    Volume,
    Network,
    Usb,
    Pnp,
    Software,
    User,
    Group,
    Session,
    Process,
    Hotfix,
    AppPackage,
    Printer,
    Monitor,
    Controller,
    Video,
    Battery,
    Sound,
    Input,
    Port,
    Antivirus,
    Firewall,
}

public sealed record CollectorExecutionResult(
    string Collector,
    InventoryCategory Category,
    CollectionState State,
    TimeSpan Duration,
    InventoryContribution? Contribution,
    string? DiagnosticCode = null,
    string? DiagnosticMessage = null);

public sealed record CollectionRunResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<CollectorExecutionResult> Results)
{
    public IReadOnlyList<InventoryContribution> Contributions => Results
        .Where(static result => result.State == CollectionState.Success && result.Contribution is not null)
        .Select(static result => result.Contribution!)
        .ToArray();

    public bool WasCancelled => Results.Any(static result => result.State == CollectionState.Cancelled);

    public bool IsComplete => Results.All(static result => result.State is CollectionState.Success or CollectionState.Unavailable or CollectionState.Disabled);
}

public sealed class CollectorFailureException : Exception
{
    public CollectorFailureException(
        CollectionState state,
        string diagnosticCode,
        string diagnosticMessage)
        : base(diagnosticMessage)
    {
        if (state is CollectionState.Success or CollectionState.Partial)
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A collector failure must use a failure state.");
        }

        State = state;
        DiagnosticCode = diagnosticCode;
    }

    public CollectionState State { get; }

    public string DiagnosticCode { get; }
}
