using DotnetGlpiAgent.Core.Identity;

namespace DotnetGlpiAgent.Core.Tests;

public sealed class IdentityStoreTests
{
    [Fact]
    public async Task LoadOrCreateAsync_PersistsCryptographicIdentityAcrossRestarts()
    {
        using var directory = new TemporaryDirectory();
        DateTimeOffset createdAt = new(2026, 7, 10, 0, 15, 30, TimeSpan.Zero);

        IdentityLoadResult first = await new IdentityStore().LoadOrCreateAsync(
            directory.Path,
            "WINDOWS-SRV.example.com",
            createdAt,
            cancellationToken: CancellationToken.None);
        IdentityLoadResult second = await new IdentityStore().LoadOrCreateAsync(
            directory.Path,
            "CHANGED-NAME",
            createdAt.AddHours(1),
            cancellationToken: CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal('4', first.Identity.AgentId.ToString("D")[14]);
        Assert.Equal("WINDOWS-SRV-2026-07-10-00-15-30", first.Identity.DeviceId);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task LoadOrCreateAsync_PreservesCorruptStateAndReportsRecovery()
    {
        using var directory = new TemporaryDirectory();
        string statePath = Path.Combine(directory.Path, IdentityStore.StateFileName);
        await File.WriteAllTextAsync(
            statePath,
            "{ not-json",
            CancellationToken.None);
        DateTimeOffset now = new(2026, 7, 10, 1, 0, 0, TimeSpan.Zero);

        IdentityLoadResult recovered = await new IdentityStore().LoadOrCreateAsync(
            directory.Path,
            "WINDOWS-SRV",
            now,
            cancellationToken: CancellationToken.None);
        IdentityLoadResult restarted = await new IdentityStore().LoadOrCreateAsync(
            directory.Path,
            "WINDOWS-SRV",
            now.AddMinutes(1),
            cancellationToken: CancellationToken.None);

        Assert.True(recovered.WasCreated);
        Assert.Contains("Corrupt identity state", Assert.Single(recovered.Warnings), StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(directory.Path, "identity.json.corrupt-*"));
        Assert.Equal(recovered.Identity, restarted.Identity);
        Assert.Empty(restarted.Warnings);
    }

    [Fact]
    public async Task LoadOrCreateAsync_ImportsRootGoJsonStateOnFirstRun()
    {
        using var directory = new TemporaryDirectory();
        string migrationFile = Path.Combine(directory.Path, "FusionInventory-Agent.json");
        await File.WriteAllTextAsync(
            migrationFile,
            """
            {
              "deviceid": "GO-HOST-2025-01-02-03-04-05",
              "agentid": "4db2772d-97d0-47c3-bfe1-597e4e65bcaf"
            }
            """,
            CancellationToken.None);

        IdentityLoadResult result = await new IdentityStore().LoadOrCreateAsync(
            Path.Combine(directory.Path, "dotnet-state"),
            "NEW-HOST",
            new DateTimeOffset(2026, 7, 10, 2, 0, 0, TimeSpan.Zero),
            migrationFile,
            CancellationToken.None);

        Assert.True(result.WasMigrated);
        Assert.Equal(Guid.Parse("4db2772d-97d0-47c3-bfe1-597e4e65bcaf"), result.Identity.AgentId);
        Assert.Equal("GO-HOST-2025-01-02-03-04-05", result.Identity.DeviceId);
    }

    [Fact]
    public async Task LoadOrCreateAsync_DoesNotOverrideExistingIdentityWithMigration()
    {
        using var directory = new TemporaryDirectory();
        DateTimeOffset now = new(2026, 7, 10, 3, 0, 0, TimeSpan.Zero);
        IdentityStore store = new();
        IdentityLoadResult existing = await store.LoadOrCreateAsync(
            directory.Path,
            "EXISTING",
            now,
            cancellationToken: CancellationToken.None);
        string migrationFile = Path.Combine(directory.Path, "migration.json");
        await File.WriteAllTextAsync(
            migrationFile,
            "{\"agentid\":\"11111111-1111-4111-8111-111111111111\",\"deviceid\":\"OTHER-2026-01-01-00-00-00\"}",
            CancellationToken.None);

        IdentityLoadResult result = await store.LoadOrCreateAsync(
            directory.Path,
            "OTHER",
            now.AddMinutes(1),
            migrationFile,
            CancellationToken.None);

        Assert.False(result.WasMigrated);
        Assert.Equal(existing.Identity, result.Identity);
    }

    [Fact]
    public async Task LoadOrCreateAsync_DegradesToGeneratedIdentityWhenMigrationIsInvalid()
    {
        using var directory = new TemporaryDirectory();
        string migrationFile = Path.Combine(directory.Path, "migration.json");
        await File.WriteAllTextAsync(
            migrationFile,
            "{\"agentid\":\"not-a-guid\",\"deviceid\":\"invalid device id\"}",
            CancellationToken.None);

        // A malformed migration file must not block identity creation
        // (the file may remain in the state directory across runs).
        IdentityLoadResult result = await new IdentityStore().LoadOrCreateAsync(
            Path.Combine(directory.Path, "state"),
            "HOST",
            DateTimeOffset.UtcNow,
            migrationFile,
            CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.False(result.WasMigrated);
        Assert.NotEqual(Guid.Empty, result.Identity.AgentId);
        Assert.Contains(result.Warnings, static warning =>
            warning.Contains("migration", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dotnet-glpi-agent-identity-{Guid.NewGuid():N}");
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
