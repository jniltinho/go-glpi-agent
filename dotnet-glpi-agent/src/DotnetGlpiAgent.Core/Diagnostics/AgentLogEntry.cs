namespace DotnetGlpiAgent.Core.Diagnostics;

public sealed record AgentLogEntry(
    DateTimeOffset TimestampUtc,
    AgentLogLevel Level,
    string EventId,
    string Message,
    string? CorrelationId,
    IReadOnlyDictionary<string, string> Properties,
    string? Exception = null);

public enum AgentLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}

public interface IAgentLogSink
{
    ValueTask WriteAsync(AgentLogEntry entry, CancellationToken cancellationToken = default);
}
