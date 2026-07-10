using DotnetGlpiAgent.Core.Configuration;

namespace DotnetGlpiAgent.Core.Tests;

public sealed class AgentConfigurationTests
{
    [Fact]
    public void Build_AppliesFileEnvironmentAndCommandLinePrecedence()
    {
        using var directory = new TemporaryDirectory();
        string includeDirectory = Path.Combine(directory.Path, "conf.d");
        Directory.CreateDirectory(includeDirectory);
        File.WriteAllText(
            Path.Combine(includeDirectory, "10-extra.cfg"),
            "tag = included\nno-category = printer\n",
            System.Text.Encoding.UTF8);
        string configurationFile = Path.Combine(directory.Path, "agent.cfg");
        File.WriteAllText(
            configurationFile,
            "server = https://file.example/front/inventory.php\n"
            + "password = file-secret\n"
            + "no-category = process\n"
            + "include \"conf.d\"\n"
            + "unknown-private-key = must-not-leak\n",
            System.Text.Encoding.UTF8);
        var environment = new Dictionary<string, string?>
        {
            ["GLPI_AGENT_SERVER"] = "https://environment.example/front/inventory.php",
            ["GLPI_AGENT_TAG"] = "environment",
        };
        var commandLine = new Dictionary<string, string?>
        {
            ["server"] = "https://cli.example/front/inventory.php",
        };

        EffectiveAgentConfiguration result = new AgentConfigurationBuilder().Build(
            configurationFile,
            environment,
            commandLine,
            directory.Path);

        Assert.Equal("https://cli.example/front/inventory.php", result.Options.Server?.AbsoluteUri);
        Assert.Equal("environment", result.Options.Tag);
        Assert.False(result.Options.IsCategoryEnabled("printer"));
        Assert.False(result.Options.IsCategoryEnabled("PROCESS"));
        Assert.Equal(ConfigurationSource.CommandLine, result.Origins["server"].Source);
        Assert.Equal(ConfigurationSource.Environment, result.Origins["tag"].Source);
        Assert.Equal("<redacted>", result.RedactedValues["password"]);
        string warning = Assert.Single(result.Warnings, static item => item.Contains("unknown-private-key", StringComparison.Ordinal));
        Assert.DoesNotContain("must-not-leak", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RedactsCredentialBearingUrlsInEffectiveValues()
    {
        using var directory = new TemporaryDirectory();
        var commandLine = new Dictionary<string, string?>
        {
            ["server"] = "https://user:pass@glpi.example/front/inventory.php",
        };

        EffectiveAgentConfiguration result = new AgentConfigurationBuilder().Build(
            null,
            new Dictionary<string, string?>(),
            commandLine,
            directory.Path);

        Assert.DoesNotContain("user:pass", result.RedactedValues["server"], StringComparison.Ordinal);
        Assert.Contains("glpi.example", result.RedactedValues["server"], StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ProcessesIncludeDirectoryInDeterministicOrder()
    {
        using var directory = new TemporaryDirectory();
        string includeDirectory = Path.Combine(directory.Path, "conf.d");
        Directory.CreateDirectory(includeDirectory);
        File.WriteAllText(Path.Combine(includeDirectory, "20-second.cfg"), "tag = second\n");
        File.WriteAllText(Path.Combine(includeDirectory, "10-first.cfg"), "tag = first\n");
        string configurationFile = Path.Combine(directory.Path, "agent.cfg");
        File.WriteAllText(configurationFile, "include = conf.d\n");

        IReadOnlyList<AgentConfigEntry> result = new AgentConfigLoader().Load(configurationFile, directory.Path);

        Assert.Collection(
            result,
            entry => Assert.Equal("first", entry.Value),
            entry => Assert.Equal("second", entry.Value));
    }

    [Fact]
    public void Load_IgnoresIncludeCycles()
    {
        using var directory = new TemporaryDirectory();
        string first = Path.Combine(directory.Path, "first.cfg");
        string second = Path.Combine(directory.Path, "second.cfg");
        File.WriteAllText(first, "tag = first\ninclude second.cfg\n");
        File.WriteAllText(second, "tag = second\ninclude first.cfg\n");

        IReadOnlyList<AgentConfigEntry> result = new AgentConfigLoader().Load(first, directory.Path);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Load_RejectsIncludeOutsideTrustedRoot()
    {
        using var directory = new TemporaryDirectory();
        string configurationDirectory = Path.Combine(directory.Path, "config");
        Directory.CreateDirectory(configurationDirectory);
        File.WriteAllText(Path.Combine(directory.Path, "outside.cfg"), "tag = outside\n");
        string configurationFile = Path.Combine(configurationDirectory, "agent.cfg");
        File.WriteAllText(configurationFile, "include ../outside.cfg\n");

        AgentConfigurationException exception = Assert.Throws<AgentConfigurationException>(
            () => new AgentConfigLoader().Load(configurationFile, configurationDirectory));

        Assert.Contains("outside the trusted configuration root", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://server.example/inventory")]
    [InlineData("file:///tmp/inventory")]
    public void Build_RejectsNonHttpServerUris(string server)
    {
        var commandLine = new Dictionary<string, string?> { ["server"] = server };

        AgentConfigurationException exception = Assert.Throws<AgentConfigurationException>(
            () => new AgentConfigurationBuilder().Build(
                environment: new Dictionary<string, string?>(),
                commandLine: commandLine));

        Assert.Contains("HTTP or HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ReportsInsecureTlsWithoutLeakingSecrets()
    {
        var commandLine = new Dictionary<string, string?>
        {
            ["no-ssl-check"] = "yes",
            ["client-cert-password"] = "very-secret",
        };

        EffectiveAgentConfiguration result = new AgentConfigurationBuilder().Build(
            environment: new Dictionary<string, string?>(),
            commandLine: commandLine);

        Assert.Contains("TLS certificate validation", Assert.Single(result.Warnings), StringComparison.Ordinal);
        Assert.Equal("<redacted>", result.RedactedValues["client-cert-password"]);
    }

    [Fact]
    public void FromWindowsRoots_UsesProgramFilesAndProgramData()
    {
        string programFiles = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "Program Files");
        string programData = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "ProgramData");

        AgentPaths paths = AgentPaths.FromWindowsRoots(programFiles, programData);

        Assert.Equal(Path.Combine(programFiles, "DotnetGlpiAgent"), paths.InstallationDirectory);
        Assert.Equal(Path.Combine(programData, "DotnetGlpiAgent", "agent.cfg"), paths.MainConfigFile);
        Assert.Equal(Path.Combine(programData, "DotnetGlpiAgent", "state"), paths.StateDirectory);
        Assert.Equal(Path.Combine(programData, "DotnetGlpiAgent", "logs"), paths.LogDirectory);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dotnet-glpi-agent-{Guid.NewGuid():N}");
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
