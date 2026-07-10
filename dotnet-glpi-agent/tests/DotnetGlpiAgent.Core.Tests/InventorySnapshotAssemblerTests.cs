using DotnetGlpiAgent.Core.Inventory;

namespace DotnetGlpiAgent.Core.Tests;

public sealed class InventorySnapshotAssemblerTests
{
    [Fact]
    public void Assemble_MergesDuplicatesAndOrdersCategoriesDeterministically()
    {
        SoftwareInfo machineSoftware = Software("Visual Studio Code", "1.100", "x64", null, "machine");
        SoftwareInfo duplicate = Software("visual studio code", "1.100", "x64", null, "user");
        SoftwareInfo second = Software("7-Zip", "24.09", "x64", null, "machine");
        InventoryContribution secondSource = new()
        {
            Source = "z-registry",
            Cpus = [Cpu("cpu-b")],
            Software = [machineSoftware, second],
        };
        InventoryContribution firstSource = new()
        {
            Source = "a-wmi",
            Cpus = [Cpu("cpu-a"), Cpu("CPU-B")],
            Software = [duplicate],
        };

        InventorySnapshot snapshot = InventorySnapshotAssembler.Assemble(
            Identity(),
            new InventoryAccount("lab"),
            Metadata(),
            [secondSource, firstSource]);

        Assert.Collection(
            snapshot.Cpus,
            item => Assert.Equal("cpu-a", item.Id),
            item => Assert.Equal("CPU-B", item.Id));
        Assert.Collection(
            snapshot.Software,
            item => Assert.Equal("7-Zip", item.Name),
            item => Assert.Equal("visual studio code", item.Name));
    }

    [Fact]
    public void Assemble_UsesStableSourcePrecedenceForSingletons()
    {
        InventoryContribution later = new()
        {
            Source = "z-source",
            OperatingSystem = OperatingSystem("later"),
        };
        InventoryContribution first = new()
        {
            Source = "a-source",
            OperatingSystem = OperatingSystem("first"),
        };

        CollectionMetadata metadata = Metadata();
        InventorySnapshot snapshot = InventorySnapshotAssembler.Assemble(
            Identity(),
            new InventoryAccount(),
            metadata,
            [later, first]);

        Assert.Equal("first", snapshot.OperatingSystem?.Name);
        Assert.Same(metadata, snapshot.Collection);
    }

    private static AgentIdentity Identity()
    {
        return new AgentIdentity(
            Guid.Parse("4db2772d-97d0-47c3-bfe1-597e4e65bcaf"),
            "device-1",
            "WINDOWS-TEST",
            "0.1.0");
    }

    private static CollectionMetadata Metadata()
    {
        DateTimeOffset started = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);
        return new CollectionMetadata(started, started.AddSeconds(3), true, [], "correlation-1");
    }

    private static CpuInfo Cpu(string id)
    {
        return new CpuInfo(id, "CPU", "Vendor", "x86_64", "0", 4, 8, 2000, 3000, null);
    }

    private static SoftwareInfo Software(
        string name,
        string version,
        string architecture,
        string? userId,
        string source)
    {
        return new SoftwareInfo(
            name,
            name,
            version,
            "Publisher",
            architecture,
            null,
            null,
            source,
            userId,
            null,
            null,
            false,
            false);
    }

    private static OperatingSystemInfo OperatingSystem(string name)
    {
        return new OperatingSystemInfo(
            name,
            "Server",
            "10.0",
            "20348",
            "21H2",
            "10.0.20348",
            "x86_64",
            "WINDOWS-TEST",
            "WORKGROUP",
            null,
            null,
            "UTC");
    }
}
