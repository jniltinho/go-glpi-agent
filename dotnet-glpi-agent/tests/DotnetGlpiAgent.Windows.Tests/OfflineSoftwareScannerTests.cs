using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Collectors;
using DotnetGlpiAgent.Windows.Registry;
using Microsoft.Win32;

namespace DotnetGlpiAgent.Windows.Tests;

public sealed class OfflineSoftwareScannerTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";

    [Fact]
    public async Task ScanAsync_LoadsMapsAndAlwaysUnloadsOfflineHive()
    {
        using var directory = new TemporaryDirectory();
        string profile = CreateProfile(directory.Path, "alice");
        var registry = new FakeRegistryQueryAdapter(Profile(profile));
        var loader = new FakeHiveLoader();
        var scanner = new OfflineSoftwareScanner(
            registry,
            loader,
            new FakeUserIdentityResolver(@"LAB\alice"),
            directory.Path);

        OfflineSoftwareScanResult result = await scanner.ScanAsync(CancellationToken.None);

        SoftwareInfo software = Assert.Single(result.Software);
        Assert.Equal("Offline Tool", software.Name);
        Assert.Equal("offline-user-registry", software.Source);
        Assert.Contains(Sid, software.UserId!, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
        Assert.True(loader.WasLoaded);
        Assert.True(loader.WasUnloaded);
        Assert.Equal(Path.Combine(profile, "NTUSER.DAT"), loader.HiveFile);
    }

    [Fact]
    public async Task ScanAsync_UnloadsHiveWhenRegistryEnumerationFails()
    {
        using var directory = new TemporaryDirectory();
        string profile = CreateProfile(directory.Path, "faulted");
        var registry = new FakeRegistryQueryAdapter(Profile(profile)) { ThrowForMountedHive = true };
        var loader = new FakeHiveLoader();
        var scanner = new OfflineSoftwareScanner(
            registry,
            loader,
            new FakeUserIdentityResolver(null),
            directory.Path);

        OfflineSoftwareScanResult result = await scanner.ScanAsync(CancellationToken.None);

        Assert.Empty(result.Software);
        SourceDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("offline-profile-scan-failed", diagnostic.Code);
        Assert.True(loader.WasUnloaded);
    }

    [Fact]
    public async Task ScanAsync_RejectsProfileOutsideTrustedRootBeforeLoading()
    {
        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        string profile = CreateProfile(outside.Path, "outside");
        var loader = new FakeHiveLoader();
        var scanner = new OfflineSoftwareScanner(
            new FakeRegistryQueryAdapter(Profile(profile)),
            loader,
            new FakeUserIdentityResolver(null),
            root.Path);

        OfflineSoftwareScanResult result = await scanner.ScanAsync(CancellationToken.None);

        Assert.Empty(result.Software);
        Assert.Single(result.Diagnostics);
        Assert.False(loader.WasLoaded);
    }

    [Fact]
    public async Task ScanAsync_ReportsPrivilegeFailureWithoutThrowing()
    {
        using var directory = new TemporaryDirectory();
        string profile = CreateProfile(directory.Path, "denied");
        var scanner = new OfflineSoftwareScanner(
            new FakeRegistryQueryAdapter(Profile(profile)),
            new FakeHiveLoader { FailWithAccessDenied = true },
            new FakeUserIdentityResolver(null),
            directory.Path);

        OfflineSoftwareScanResult result = await scanner.ScanAsync(CancellationToken.None);

        SourceDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(CollectionState.AccessDenied, diagnostic.State);
        Assert.Empty(result.Software);
    }

    private static string CreateProfile(string root, string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "NTUSER.DAT"), "sanitized test hive placeholder");
        return path;
    }

    private static RegistryKeySnapshot Profile(string path)
    {
        return new RegistryKeySnapshot(
            $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{Sid}",
            new Dictionary<string, object?> { ["ProfileImagePath"] = path });
    }

    private sealed class FakeRegistryQueryAdapter(RegistryKeySnapshot profile) : IRegistryQueryAdapter
    {
        public bool ThrowForMountedHive { get; init; }

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
            if (path.EndsWith("ProfileList", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult<IReadOnlyList<RegistryKeySnapshot>>([profile]);
            }

            if (path.Length == 0)
            {
                return ValueTask.FromResult<IReadOnlyList<RegistryKeySnapshot>>([]);
            }

            if (ThrowForMountedHive)
            {
                return ValueTask.FromException<IReadOnlyList<RegistryKeySnapshot>>(
                    new IOException("Injected registry enumeration failure."));
            }

            if (path.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult<IReadOnlyList<RegistryKeySnapshot>>([]);
            }

            return ValueTask.FromResult<IReadOnlyList<RegistryKeySnapshot>>(
            [
                new RegistryKeySnapshot(
                    $"{path}\\OfflineTool",
                    new Dictionary<string, object?>
                    {
                        ["DisplayName"] = "Offline Tool",
                        ["DisplayVersion"] = "1.0",
                    }),
            ]);
        }
    }

    private sealed class FakeHiveLoader : IRegistryHiveLoader
    {
        public bool FailWithAccessDenied { get; init; }

        public bool WasLoaded { get; private set; }

        public bool WasUnloaded { get; private set; }

        public string? HiveFile { get; private set; }

        public ValueTask<IDisposable> LoadAsync(
            string mountName,
            string hiveFile,
            CancellationToken cancellationToken)
        {
            if (FailWithAccessDenied)
            {
                return ValueTask.FromException<IDisposable>(new CollectorFailureException(
                    CollectionState.AccessDenied,
                    "registry-hive-privilege-unavailable",
                    "Privilege unavailable."));
            }

            WasLoaded = true;
            HiveFile = hiveFile;
            return ValueTask.FromResult<IDisposable>(new CallbackDisposable(() => WasUnloaded = true));
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            callback();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private sealed class FakeUserIdentityResolver(string? account) : IWindowsUserIdentityResolver
    {
        public string? ResolveAccountName(string sid) => account;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dotnet-glpi-agent-profiles-{Guid.NewGuid():N}");
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
