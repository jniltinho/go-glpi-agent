using System.Text.Json;

using DotnetGlpiAgent.Core.Diagnostics;

namespace DotnetGlpiAgent.Core.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Redact_RemovesConfiguredAndStructuredSecrets()
    {
        var redactor = new SecretRedactor(["configured-secret"]);
        const string message = "Authorization: Bearer abc.def password=hunter2 "
            + "url=https://user:pass@example.test configured-secret";

        string result = redactor.Redact(message);

        Assert.DoesNotContain("abc.def", result, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pass", result, StringComparison.Ordinal);
        Assert.DoesNotContain("configured-secret", result, StringComparison.Ordinal);
        Assert.Contains("<redacted>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginScope_RestoresNestedCorrelationIds()
    {
        Assert.Null(CorrelationContext.Current);
        using (CorrelationContext.BeginScope("outer"))
        {
            Assert.Equal("outer", CorrelationContext.Current);
            using (CorrelationContext.BeginScope("inner"))
            {
                Assert.Equal("inner", CorrelationContext.Current);
            }

            Assert.Equal("outer", CorrelationContext.Current);
        }

        Assert.Null(CorrelationContext.Current);
    }

    [Fact]
    public async Task LogAsync_RedactsMessagePropertiesAndExceptionWithinCorrelationScope()
    {
        var sink = new CapturingSink();
        var logger = new AgentLogger([sink], new SecretRedactor(["shared-secret"]));
        using (CorrelationContext.BeginScope("request-123"))
        {
            await logger.LogAsync(
                AgentLogLevel.Error,
                "transport.failed",
                "Request password=secret-value shared-secret",
                new Dictionary<string, string>
                {
                    ["authorization"] = "Bearer token-value",
                    ["endpoint"] = "https://example.test/front/inventory.php",
                },
                new InvalidOperationException("shared-secret"));
        }

        AgentLogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal("request-123", entry.CorrelationId);
        Assert.Equal("<redacted>", entry.Properties["authorization"]);
        Assert.DoesNotContain("secret-value", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("shared-secret", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("shared-secret", entry.Exception!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventLogSink_ForwardsOnlyHighSeverityEntries()
    {
        var writer = new CapturingEventWriter();
        var sink = new EventLogSink(writer);
        AgentLogEntry information = Entry(AgentLogLevel.Information, "information");
        AgentLogEntry warning = Entry(AgentLogLevel.Warning, "warning") with { CorrelationId = "cycle-1" };

        await sink.WriteAsync(information);
        await sink.WriteAsync(warning);

        CapturedEvent captured = Assert.Single(writer.Events);
        Assert.Equal(AgentLogLevel.Warning, captured.Level);
        Assert.Contains("correlation=cycle-1", captured.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RollingFileLogSink_RotatesRetainsAndHardensFiles()
    {
        using var directory = new TemporaryDirectory();
        var hardener = new CapturingPermissionHardener();
        using var sink = new RollingFileLogSink(directory.Path, "agent.log", 420, 2, hardener);
        var logger = new AgentLogger([sink], new SecretRedactor(["do-not-store"]));

        for (int index = 0; index < 12; index++)
        {
            await logger.LogAsync(
                AgentLogLevel.Information,
                "inventory.cycle",
                $"cycle={index} password=secret-{index} do-not-store",
                new Dictionary<string, string> { ["token"] = $"token-{index}" });
        }

        string[] logFiles = Directory.EnumerateFiles(directory.Path, "agent.log*").ToArray();
        Assert.InRange(logFiles.Length, 2, 3);
        string content = string.Join('\n', logFiles.Select(File.ReadAllText));
        Assert.DoesNotContain("secret-", content, StringComparison.Ordinal);
        Assert.DoesNotContain("token-", content, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-store", content, StringComparison.Ordinal);
        string[] lines = logFiles.SelectMany(File.ReadAllLines).ToArray();
        Assert.Contains(lines, static line =>
        {
            using JsonDocument document = JsonDocument.Parse(line);
            string? message = document.RootElement.GetProperty("message").GetString();
            return message?.Contains("<redacted>", StringComparison.Ordinal) == true;
        });
        Assert.Contains(directory.Path, hardener.Directories);
        Assert.Contains(Path.Combine(directory.Path, "agent.log"), hardener.Files);
    }

    private static AgentLogEntry Entry(AgentLogLevel level, string message)
    {
        return new AgentLogEntry(
            DateTimeOffset.UtcNow,
            level,
            "test.event",
            message,
            null,
            new Dictionary<string, string>());
    }

    private sealed class CapturingSink : IAgentLogSink
    {
        public List<AgentLogEntry> Entries { get; } = [];

        public ValueTask WriteAsync(AgentLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record CapturedEvent(AgentLogLevel Level, string EventId, string Message);

    private sealed class CapturingEventWriter : IHostEventWriter
    {
        public List<CapturedEvent> Events { get; } = [];

        public ValueTask WriteAsync(
            AgentLogLevel level,
            string eventId,
            string message,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new CapturedEvent(level, eventId, message));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingPermissionHardener : IFilePermissionHardener
    {
        public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

        public void HardenDirectory(string path) => Directories.Add(path);

        public void HardenFile(string path) => Files.Add(path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dotnet-glpi-agent-logs-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
            GC.SuppressFinalize(this);
        }
    }
}
