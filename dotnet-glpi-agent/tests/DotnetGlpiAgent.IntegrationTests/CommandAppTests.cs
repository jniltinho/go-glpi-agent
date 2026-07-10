using DotnetGlpiAgent.App.Cli;
using DotnetGlpiAgent.App.Runtime;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Transport;

namespace DotnetGlpiAgent.IntegrationTests;

public sealed class CommandAppTests
{
    [Fact]
    public async Task Version_WritesVersionToStdoutOnly()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CommandApp(new FakeRuntimeFactory(Snapshot()), output, error);

        int result = await app.RunAsync(["version"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, result);
        Assert.StartsWith("Dotnet GLPI Agent ", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ValidateConfig_RedactsSecretsAndWritesWarningsToStderr()
    {
        string directory = TemporaryDirectory();
        try
        {
            string config = Path.Combine(directory, "agent.cfg");
            await File.WriteAllTextAsync(
                config,
                "server = https://glpi.example/front/inventory.php\npassword = super-secret\nno-ssl-check = true\n");
            var output = new StringWriter();
            var error = new StringWriter();
            var app = new CommandApp(new FakeRuntimeFactory(Snapshot()), output, error);

            int result = await app.RunAsync(["validate-config", "--config", config], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, result);
            Assert.Contains("password=<redacted>", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("super-secret", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("TLS certificate validation", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Run_LocalTarget_WritesBothFormatsFromCollectedSnapshot()
    {
        string directory = TemporaryDirectory();
        try
        {
            var factory = new FakeRuntimeFactory(Snapshot());
            var output = new StringWriter();
            var error = new StringWriter();
            var app = new CommandApp(factory, output, error);

            int result = await app.RunAsync(["run", "--local", directory], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, result);
            Assert.True(factory.Runtime.Collected);
            Assert.False(factory.Runtime.Submitted);
            Assert.True(File.Exists(Path.Combine(directory, "HOST-2026-07-10-12-00-00.json")));
            Assert.True(File.Exists(Path.Combine(directory, "HOST-2026-07-10-12-00-00.xml")));
            Assert.Contains("inventory JSON:", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Run_IndeterminateSubmission_ReturnsDedicatedExitCode()
    {
        var factory = new FakeRuntimeFactory(
            Snapshot(),
            new GlpiSubmissionResult(
                GlpiProtocolKind.NativeJson,
                SubmissionState.Indeterminate,
                DiagnosticCode: "inventory-upload-indeterminate"));
        var app = new CommandApp(factory, new StringWriter(), new StringWriter());

        int result = await app.RunAsync(
            ["run", "--server", "https://glpi.example/front/inventory.php"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.Indeterminate, result);
        Assert.True(factory.Runtime.Submitted);
    }

    [Theory]
    [InlineData("unknown-command")]
    [InlineData("run --unknown")]
    [InlineData("run --server")]
    public async Task InvalidCommandLine_ReturnsUsage(string commandLine)
    {
        var error = new StringWriter();
        var app = new CommandApp(new FakeRuntimeFactory(Snapshot()), new StringWriter(), error);

        int result = await app.RunAsync(commandLine.Split(' '), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, result);
        Assert.StartsWith("error:", error.ToString(), StringComparison.Ordinal);
    }

    private static InventorySnapshot Snapshot()
    {
        return new InventorySnapshot
        {
            Identity = new AgentIdentity(
                Guid.Parse("3c33b875-71b4-4016-a95a-e62d012c4e5b"),
                "HOST-2026-07-10-12-00-00",
                "HOST",
                "Dotnet-GLPI-Agent/0.1.0"),
            Collection = new CollectionMetadata(
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                true,
                [],
                "command-test"),
        };
    }

    private static string TemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dotnet-glpi-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeRuntimeFactory : IAppRuntimeFactory
    {
        public FakeRuntimeFactory(
            InventorySnapshot snapshot,
            GlpiSubmissionResult? submission = null)
        {
            Runtime = new FakeRuntime(
                snapshot,
                submission ?? new GlpiSubmissionResult(GlpiProtocolKind.NativeJson, SubmissionState.Accepted, 200));
        }

        public FakeRuntime Runtime { get; }

        public IAppRuntime Create(AgentOptions options) => Runtime;
    }

    private sealed class FakeRuntime(
        InventorySnapshot snapshot,
        GlpiSubmissionResult submission) : IAppRuntime
    {
        public bool Collected { get; private set; }

        public bool Submitted { get; private set; }

        public ValueTask<InventorySnapshot> CollectAsync(
            AgentOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Collected = true;
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask<GlpiSubmissionResult> SubmitAsync(
            InventorySnapshot inventory,
            AgentOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Submitted = true;
            return ValueTask.FromResult(submission);
        }
    }
}
