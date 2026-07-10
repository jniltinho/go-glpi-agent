using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.AppPackages;
using DotnetGlpiAgent.Windows.Collectors;

namespace DotnetGlpiAgent.Windows.Tests;

public sealed class AppPackageCollectorTests
{
    [Fact]
    public async Task CollectAsync_MapsPackagesAndPreservesPerProfileDiagnostics()
    {
        var diagnostic = new SourceDiagnostic(
            "appx:S-1-5-21-2",
            CollectionState.AccessDenied,
            "appx-access-denied",
            "Access denied.");
        var adapter = new FakeAppPackageDataAdapter(new AppPackageEnumerationResult(
            [Package("Example.App_1", "Example.App", "1.2.3.4", "X64", "S-1-5-21-1")],
            [diagnostic]));
        var collector = new AppPackageCollector(adapter, SupportedPlatform.Instance);

        InventoryContribution result = await collector.CollectAsync(Context(), CancellationToken.None);

        AppPackageInfo package = Assert.Single(result.AppPackages);
        Assert.Equal("Example.App", package.Name);
        Assert.Equal("x86_64", package.Architecture);
        Assert.Equal("S-1-5-21-1", package.UserId);
        Assert.Equal(diagnostic, Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void Map_SkipsMalformedRowsAndDeduplicatesCaseInsensitively()
    {
        AppPackageDataSnapshot valid = Package("Example.App_1", "Example.App", "1.0.0.0", "X64", "S-1-5-21-1");
        AppPackageDataSnapshot duplicate = Package("different-full-name", "example.app", "1.0.0.0", "x64", "S-1-5-21-1");
        AppPackageDataSnapshot differentUser = Package("Example.App_2", "Example.App", "1.0.0.0", "X64", "S-1-5-21-2");
        AppPackageDataSnapshot differentArchitecture = Package("Example.App_3", "Example.App", "1.0.0.0", "Arm64", "S-1-5-21-1");
        AppPackageDataSnapshot missingName = Package("bad", " ", null, null, "S-1-5-21-1");

        AppPackageInfo[] result = AppPackageCollector.Map(
            [duplicate, missingName, differentUser, differentArchitecture, valid]);
        AppPackageInfo[] reversed = AppPackageCollector.Map(
            [valid, differentArchitecture, differentUser, missingName, duplicate]);

        Assert.Equal(3, result.Length);
        Assert.Equal(result, reversed);
        Assert.Single(result, static item =>
            item.Name.Equals("Example.App", StringComparison.OrdinalIgnoreCase)
            && item.UserId == "S-1-5-21-1"
            && item.Architecture == "x86_64");
        Assert.Contains(result, static item => item.UserId == "S-1-5-21-2");
        Assert.Contains(result, static item => item.Architecture == "arm64");
    }

    [Fact]
    public void SourceCode_DoesNotInvokeShellForAppxCollection()
    {
        string source = File.ReadAllText(Path.Combine(
            FindProjectRoot(),
            "src",
            "DotnetGlpiAgent.Windows",
            "AppPackages",
            "AppPackageDataAdapter.cs"));

        Assert.DoesNotContain("powershell", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.Contains("PackageManager", source, StringComparison.Ordinal);
    }

    private static AppPackageDataSnapshot Package(
        string id,
        string name,
        string? version,
        string? architecture,
        string sid)
    {
        return new AppPackageDataSnapshot(id, name, version, "CN=Example", architecture, sid, false);
    }

    private static CollectorContext Context()
    {
        return new CollectorContext(new AgentOptions(), DateTimeOffset.UtcNow.AddMinutes(1), "appx-fixture");
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DotnetGlpiAgent.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the .NET project root.");
    }

    private sealed class FakeAppPackageDataAdapter(AppPackageEnumerationResult result) : IAppPackageDataAdapter
    {
        public ValueTask<AppPackageEnumerationResult> EnumerateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SupportedPlatform : IWindowsPlatform
    {
        public static SupportedPlatform Instance { get; } = new();

        public bool IsWindows => true;
    }
}
