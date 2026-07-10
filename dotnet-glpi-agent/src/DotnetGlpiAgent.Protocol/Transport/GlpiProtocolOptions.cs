using DotnetGlpiAgent.Core.Configuration;

namespace DotnetGlpiAgent.Protocol.Transport;

public sealed record GlpiProtocolOptions
{
    public required Uri Server { get; init; }

    public CompressionKind Compression { get; init; } = CompressionKind.Auto;

    public string? UserName { get; init; }

    public string? Password { get; init; }

    public bool Lazy { get; init; }

    public bool Force { get; init; }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaximumPollDuration { get; init; } = TimeSpan.FromMinutes(2);

    public int MaximumPollAttempts { get; init; } = 120;

    public int MaximumControlRetries { get; init; } = 2;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}

public enum GlpiProtocolKind
{
    NativeJson,
    LegacyXml,
}

public enum SubmissionState
{
    Accepted,
    Skipped,
    Rejected,
    Indeterminate,
}

public sealed record GlpiSubmissionResult(
    GlpiProtocolKind Protocol,
    SubmissionState State,
    int? StatusCode = null,
    string? DiagnosticCode = null);

public enum GlpiFailureKind
{
    Configuration,
    Authentication,
    Tls,
    Timeout,
    RateLimited,
    Server,
    Schema,
    Protocol,
    Transport,
    Cancelled,
}

public sealed class GlpiProtocolException : Exception
{
    public GlpiProtocolException(GlpiFailureKind kind, string diagnosticCode, string message)
        : base(message)
    {
        Kind = kind;
        DiagnosticCode = diagnosticCode;
    }

    public GlpiProtocolException(GlpiFailureKind kind, string diagnosticCode, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
        DiagnosticCode = diagnosticCode;
    }

    public GlpiFailureKind Kind { get; }

    public string DiagnosticCode { get; }
}
