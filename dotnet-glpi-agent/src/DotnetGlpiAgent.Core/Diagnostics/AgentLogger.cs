namespace DotnetGlpiAgent.Core.Diagnostics;

public sealed class AgentLogger
{
    private readonly IReadOnlyList<IAgentLogSink> _sinks;
    private readonly SecretRedactor _redactor;
    private readonly TimeProvider _timeProvider;

    public AgentLogger(
        IEnumerable<IAgentLogSink> sinks,
        SecretRedactor redactor,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        _sinks = sinks.ToArray();
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask LogAsync(
        AgentLogLevel level,
        string eventId,
        string message,
        IReadOnlyDictionary<string, string>? properties = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentNullException.ThrowIfNull(message);

        var entry = new AgentLogEntry(
            _timeProvider.GetUtcNow(),
            level,
            eventId,
            _redactor.Redact(message),
            CorrelationContext.Current,
            _redactor.RedactProperties(properties),
            exception is null ? null : _redactor.Redact(exception.ToString()));

        foreach (IAgentLogSink sink in _sinks)
        {
            await sink.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }
}
