namespace DotnetGlpiAgent.Core.Diagnostics;

public interface IHostEventWriter
{
    ValueTask WriteAsync(
        AgentLogLevel level,
        string eventId,
        string message,
        CancellationToken cancellationToken = default);
}

public sealed class EventLogSink(
    IHostEventWriter writer,
    AgentLogLevel minimumLevel = AgentLogLevel.Warning) : IAgentLogSink
{
    public ValueTask WriteAsync(AgentLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Level < minimumLevel
            ? ValueTask.CompletedTask
            : writer.WriteAsync(
                entry.Level,
                entry.EventId,
                FormatMessage(entry),
                cancellationToken);
    }

    private static string FormatMessage(AgentLogEntry entry)
    {
        string correlation = entry.CorrelationId is null
            ? string.Empty
            : $" correlation={entry.CorrelationId}";
        return $"{entry.Message}{correlation}";
    }
}
