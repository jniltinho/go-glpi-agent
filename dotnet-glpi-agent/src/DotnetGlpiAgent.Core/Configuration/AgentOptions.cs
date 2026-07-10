namespace DotnetGlpiAgent.Core.Configuration;

public sealed record AgentOptions
{
    public Uri? Server { get; init; }

    public string? LocalPath { get; init; }

    public string? Tag { get; init; }

    public TimeSpan Delay { get; init; } = TimeSpan.FromHours(1);

    public bool Lazy { get; init; }

    public bool Force { get; init; }

    public TimeSpan CollectorTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public IReadOnlySet<string> ExcludedCategories { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool ScanProcesses { get; init; }

    public bool ScanProfiles { get; init; }

    public string? StateDirectory { get; init; }

    public string? UserName { get; init; }

    public string? Password { get; init; }

    public Uri? Proxy { get; init; }

    public string? ProxyUserName { get; init; }

    public string? ProxyPassword { get; init; }

    public bool AllowInsecureTls { get; init; }

    public string? CaCertificateFile { get; init; }

    public string? ClientCertificateFile { get; init; }

    public string? ClientCertificatePassword { get; init; }

    public CompressionKind Compression { get; init; } = CompressionKind.Auto;

    public LogProvider Logger { get; init; } = LogProvider.Console;

    public string? LogFile { get; init; }

    public bool Debug { get; init; }

    public int MaxConcurrency { get; init; } = 4;

    public bool IsCategoryEnabled(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return !ExcludedCategories.Contains(category);
    }
}

public enum CompressionKind
{
    Auto,
    Gzip,
    Zlib,
    None,
}

public enum LogProvider
{
    Console,
    File,
    EventLog,
}

public enum ConfigurationSource
{
    Default,
    File,
    Environment,
    CommandLine,
}

public sealed record ConfigurationOrigin(
    ConfigurationSource Source,
    string? Location = null,
    int? LineNumber = null);

public sealed record EffectiveAgentConfiguration(
    AgentOptions Options,
    IReadOnlyDictionary<string, ConfigurationOrigin> Origins,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> RedactedValues);
