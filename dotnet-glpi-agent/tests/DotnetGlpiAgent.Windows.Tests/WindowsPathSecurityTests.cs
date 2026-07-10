using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Windows.Security;

namespace DotnetGlpiAgent.Windows.Tests;

public sealed class WindowsPathSecurityTests
{
    [Fact]
    public void ValidateTrustedPath_AcceptsProtectedPathInsideRoot()
    {
        using var directory = new TemporaryDirectory();
        string configuration = Path.Combine(directory.Path, "agent.cfg");
        File.WriteAllText(configuration, "tag = lab\n");
        var policy = new WindowsPathSecurityPolicy(new FakeInspector());

        string result = policy.ValidateTrustedPath(configuration, directory.Path, "configuration");

        Assert.Equal(Path.GetFullPath(configuration), result);
    }

    [Fact]
    public void ValidateTrustedPath_RejectsUnprivilegedWritableParent()
    {
        using var directory = new TemporaryDirectory();
        string configuration = Path.Combine(directory.Path, "conf.d", "agent.cfg");
        var policy = new WindowsPathSecurityPolicy(new FakeInspector(directory.Path));

        AgentConfigurationException exception = Assert.Throws<AgentConfigurationException>(
            () => policy.ValidateTrustedPath(configuration, directory.Path, "configuration"));

        Assert.Contains("unprivileged Windows principal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateTrustedPath_RejectsPathOutsideRootBeforeAclInspection()
    {
        using var directory = new TemporaryDirectory();
        string outside = Path.Combine(Path.GetDirectoryName(directory.Path)!, "outside.cfg");
        var inspector = new FakeInspector();
        var policy = new WindowsPathSecurityPolicy(inspector);

        Assert.Throws<AgentConfigurationException>(
            () => policy.ValidateTrustedPath(outside, directory.Path, "configuration"));
        Assert.Empty(inspector.InspectedPaths);
    }

    private sealed class FakeInspector(params string[] writablePaths) : IFileAccessControlInspector
    {
        private readonly HashSet<string> _writablePaths = writablePaths
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public List<string> InspectedPaths { get; } = [];

        public bool IsUnprivilegedWritable(string path)
        {
            string fullPath = Path.GetFullPath(path);
            InspectedPaths.Add(fullPath);
            return _writablePaths.Contains(fullPath);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dotnet-glpi-agent-acl-{Guid.NewGuid():N}");
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
