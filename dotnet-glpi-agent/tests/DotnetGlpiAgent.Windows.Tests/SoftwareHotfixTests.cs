using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Collectors;
using DotnetGlpiAgent.Windows.Management;
using DotnetGlpiAgent.Windows.Registry;
using Microsoft.Win32;

namespace DotnetGlpiAgent.Windows.Tests;

public sealed class SoftwareHotfixTests
{
    private const string MachinePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    [Fact]
    public async Task SoftwareCollector_EnumeratesNativeWow64AndLoadedUserHives()
    {
        const string sid = "S-1-5-21-1000-1000-1000-1001";
        var registry = new FakeRegistryQueryAdapter();
        registry.Add(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            MachinePath,
            Snapshot(@"SOFTWARE\...\App64", ("DisplayName", "Example App"), ("DisplayVersion", "1.0"), ("Publisher", "Example"), ("InstallDate", "20260701"), ("EstimatedSize", 2048)));
        registry.Add(
            RegistryHive.LocalMachine,
            RegistryView.Registry32,
            MachinePath,
            Snapshot(@"SOFTWARE\...\App32", ("DisplayName", "Example App"), ("DisplayVersion", "1.0"), ("Publisher", "Example")));
        registry.Add(
            RegistryHive.Users,
            RegistryView.Default,
            string.Empty,
            Snapshot($@"\{sid}"));
        registry.Add(
            RegistryHive.Users,
            RegistryView.Default,
            $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Uninstall",
            Snapshot($@"{sid}\...\UserApp", ("DisplayName", "User Tool"), ("DisplayVersion", "2.0")));
        registry.Add(
            RegistryHive.Users,
            RegistryView.Default,
            $@"{sid}\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
        var collector = new SoftwareCollector(
            registry,
            new FakeUserIdentityResolver(@"LAB\alice"),
            SupportedPlatform.Instance);

        InventoryContribution result = await collector.CollectAsync(Context(), CancellationToken.None);

        Assert.Equal(3, result.Software.Count);
        Assert.Contains(result.Software, static item => item.Name == "Example App" && item.Architecture == "x86_64");
        Assert.Contains(result.Software, static item => item.Name == "Example App" && item.Architecture == "x86");
        SoftwareInfo user = Assert.Single(result.Software, static item => item.Name == "User Tool");
        Assert.Contains(sid, user.UserId!, StringComparison.Ordinal);
        Assert.Contains(@"LAB\alice", user.UserId!, StringComparison.Ordinal);
        SoftwareInfo machine = Assert.Single(result.Software, static item => item.Name == "Example App" && item.Architecture == "x86_64");
        Assert.Equal(2097152UL, machine.EstimatedSizeBytes);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), machine.InstallDate);
        Assert.Contains(registry.Calls, static call => call.Hive == RegistryHive.LocalMachine && call.View == RegistryView.Registry64);
        Assert.Contains(registry.Calls, static call => call.Hive == RegistryHive.LocalMachine && call.View == RegistryView.Registry32);
    }

    [Fact]
    public void MapRows_SkipsMissingNamesAndToleratesMalformedFields()
    {
        RegistryKeySnapshot[] rows =
        [
            Snapshot("missing-name", ("DisplayVersion", "1.0")),
            Snapshot(
                "malformed",
                ("DisplayName", "Malformed App"),
                ("DisplayVersion", ""),
                ("InstallDate", "not-a-date"),
                ("EstimatedSize", "huge"),
                ("SystemComponent", 1)),
        ];

        SoftwareInfo result = Assert.Single(SoftwareCollector.MapRows(rows, "x86_64", "machine-registry", null, null));

        Assert.Equal("Malformed App", result.Name);
        Assert.Null(result.Version);
        Assert.Null(result.InstallDate);
        Assert.Null(result.EstimatedSizeBytes);
        Assert.True(result.IsSystemComponent);
    }

    [Fact]
    public void Deduplicate_PreservesArchitectureAndUserDistinctions()
    {
        SoftwareInfo machine64 = Software("App", "1", "x86_64", null, "machine-registry");
        SoftwareInfo duplicate = Software("app", "1", "x86_64", null, "user-registry");
        SoftwareInfo machine32 = Software("App", "1", "x86", null, "machine-registry");
        SoftwareInfo user = Software("App", "1", "x86_64", "LAB\\alice", "user-registry");

        SoftwareInfo[] result = SoftwareCollector.Deduplicate([duplicate, machine64, machine32, user]);

        Assert.Equal(3, result.Length);
        Assert.Contains(machine64, result);
        Assert.Contains(machine32, result);
        Assert.Contains(user, result);
    }

    [Theory]
    [InlineData("Security Update", "Security Update")]
    [InlineData("Hotfix", "Hotfix")]
    [InlineData("Update", "Update")]
    public void HotfixMap_ClassifiesUpdates(string description, string expected)
    {
        WmiRow row = Row(
            ("HotFixID", "KB5000001"),
            ("Description", description),
            ("InstalledBy", @"NT AUTHORITY\SYSTEM"),
            // Win32_QuickFixEngineering reports InstalledOn as en-US M/d/yyyy.
            ("InstalledOn", "7/1/2026"));

        HotfixInfo result = HotfixCollector.Map(row)!;

        Assert.Equal(expected, result.Classification);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), result.InstalledAt);
    }

    [Fact]
    public void SourceCode_DoesNotUseWin32Product()
    {
        string sourceRoot = Path.Combine(FindProjectRoot(), "src");
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("Win32_Product", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        }
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

    private static CollectorContext Context()
    {
        return new CollectorContext(new AgentOptions(), DateTimeOffset.UtcNow.AddMinutes(1), "software-fixture");
    }

    private static RegistryKeySnapshot Snapshot(string path, params (string Key, object? Value)[] values)
    {
        return new RegistryKeySnapshot(
            path,
            values.ToDictionary(static pair => pair.Key, static pair => pair.Value));
    }

    private static WmiRow Row(params (string Key, object? Value)[] values)
    {
        return new WmiRow(values.ToDictionary(static pair => pair.Key, static pair => pair.Value));
    }

    private static SoftwareInfo Software(
        string name,
        string version,
        string architecture,
        string? user,
        string source)
    {
        return new SoftwareInfo(
            name,
            name,
            version,
            null,
            architecture,
            null,
            null,
            source,
            user,
            null,
            null,
            false,
            false);
    }

    private sealed class FakeRegistryQueryAdapter : IRegistryQueryAdapter
    {
        private readonly Dictionary<RegistryCall, IReadOnlyList<RegistryKeySnapshot>> _rows = [];

        public List<RegistryCall> Calls { get; } = [];

        public void Add(
            RegistryHive hive,
            RegistryView view,
            string path,
            params RegistryKeySnapshot[] rows)
        {
            _rows[new RegistryCall(hive, view, path)] = rows;
        }

        public ValueTask<RegistryKeySnapshot?> ReadKeyAsync(
            RegistryHive hive,
            RegistryView view,
            string path,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<RegistryKeySnapshot?>(null);
        }

        public ValueTask<IReadOnlyList<RegistryKeySnapshot>> EnumerateSubKeysAsync(
            RegistryHive hive,
            RegistryView view,
            string path,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken)
        {
            var call = new RegistryCall(hive, view, path);
            Calls.Add(call);
            return ValueTask.FromResult(_rows.TryGetValue(call, out IReadOnlyList<RegistryKeySnapshot>? rows)
                ? rows
                : (IReadOnlyList<RegistryKeySnapshot>)[]);
        }
    }

    private sealed record RegistryCall(RegistryHive Hive, RegistryView View, string Path);

    private sealed class FakeUserIdentityResolver(string account) : IWindowsUserIdentityResolver
    {
        public string? ResolveAccountName(string sid) => account;
    }

    private sealed class SupportedPlatform : IWindowsPlatform
    {
        public static SupportedPlatform Instance { get; } = new();

        public bool IsWindows => true;
    }
}
