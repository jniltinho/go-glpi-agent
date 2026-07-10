using System.Text.Json;
using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Bcl;
using DotnetGlpiAgent.Windows.Collectors;
using DotnetGlpiAgent.Windows.Management;
using DotnetGlpiAgent.Windows.Registry;
using Microsoft.Win32;

namespace DotnetGlpiAgent.Windows.Tests;

public sealed class CoreCollectorFixtureTests
{
    private static readonly string[] RegistryMultiString = ["A", "B"];

    [Fact]
    public async Task CoreCollectors_MapSanitizedWindowsServerFixture()
    {
        using Fixture fixture = Fixture.Load();
        JsonElement normal = fixture.Root.GetProperty("normal");
        var wmi = new FakeWmiQueryAdapter(ReadWmi(normal.GetProperty("wmi")));
        var registry = new FakeRegistryQueryAdapter(RegistryRow(normal.GetProperty("registry")));
        var host = new FakeHostDataAdapter(ReadHost(normal.GetProperty("host")));
        CollectorContext context = Context();

        InventoryContribution os = await new OperatingSystemCollector(wmi, registry, host, SupportedPlatform.Instance)
            .CollectAsync(context, CancellationToken.None);
        InventoryContribution hardware = await new HardwareCollector(wmi, SupportedPlatform.Instance)
            .CollectAsync(context, CancellationToken.None);
        InventoryContribution bios = await new BiosCollector(wmi, SupportedPlatform.Instance)
            .CollectAsync(context, CancellationToken.None);
        InventoryContribution cpu = await new CpuCollector(wmi, SupportedPlatform.Instance)
            .CollectAsync(context, CancellationToken.None);
        InventoryContribution memory = await new MemoryCollector(wmi, SupportedPlatform.Instance)
            .CollectAsync(context, CancellationToken.None);

        Assert.Equal("Windows Server 2022 Standard", os.OperatingSystem?.Name);
        Assert.Equal("20348", os.OperatingSystem?.Build);
        Assert.Equal(4052, os.OperatingSystem?.Ubr);
        Assert.Equal("x86_64", os.OperatingSystem?.Architecture);
        Assert.Equal("LAB-SERIAL-001", hardware.Hardware?.SerialNumber);
        Assert.Equal("4db2772d-97d0-47c3-bfe1-597e4e65bcaf", hardware.Hardware?.SystemUuid);
        Assert.Equal("Server", hardware.Hardware?.ChassisType);
        Assert.Equal(4294967296UL, hardware.Hardware?.SwapTotalBytes);
        Assert.Equal("3.4", bios.Bios?.SmBiosVersion);
        Assert.Equal("x86_64", Assert.Single(cpu.Cpus).Architecture);
        Assert.Null(Assert.Single(cpu.Cpus).SerialNumber);
        Assert.Collection(
            memory.MemoryModules,
            item =>
            {
                Assert.Equal(4294967296UL, item.CapacityBytes);
                Assert.Equal("DDR4", item.MemoryType);
                Assert.False(item.IsEmptySlot);
            },
            item => Assert.True(item.IsEmptySlot));
        Assert.All(wmi.Queries, static query => Assert.DoesNotContain('*', query.ToWql()));
    }

    [Fact]
    public void HardwareMap_RemovesVirtualBoxPlaceholderIdentityValues()
    {
        using Fixture fixture = Fixture.Load();
        JsonElement scenario = fixture.Root.GetProperty("virtualBoxPlaceholders");

        HardwareInfo result = HardwareCollector.Map(
            Row(scenario.GetProperty("system")),
            Row(scenario.GetProperty("product")),
            Row(scenario.GetProperty("board")),
            Row(scenario.GetProperty("enclosure")));

        Assert.Equal("innotek GmbH", result.Manufacturer);
        Assert.Equal("VirtualBox", result.Model);
        Assert.Null(result.SerialNumber);
        Assert.Null(result.SystemUuid);
        Assert.Null(result.AssetTag);
        Assert.Null(result.BaseboardSerialNumber);
    }

    [Fact]
    public void CoreMappings_DegradeMalformedNumbersWithoutThrowing()
    {
        using Fixture fixture = Fixture.Load();
        JsonElement scenario = fixture.Root.GetProperty("malformed");

        CpuInfo cpu = CpuCollector.Map(Row(scenario.GetProperty("cpu")));
        MemoryModuleInfo memory = Assert.Single(MemoryCollector.Map(
            [Row(scenario.GetProperty("memory"))],
            1));

        Assert.Null(cpu.Architecture);
        Assert.Null(cpu.Cores);
        Assert.Null(cpu.LogicalProcessors);
        Assert.Null(cpu.CurrentClockMhz);
        Assert.Null(memory.CapacityBytes);
        Assert.Null(memory.SpeedMhz);
        Assert.Equal("Other (999)", memory.MemoryType);
    }

    [Fact]
    public async Task Orchestrator_CategorizesWmiAccessDeniedAndTimeoutFailures()
    {
        var denied = new BiosCollector(
            new ThrowingWmiQueryAdapter(new CollectorFailureException(
                CollectionState.AccessDenied,
                "wmi-access-denied",
                "Access denied.")),
            SupportedPlatform.Instance);
        var timedOut = new CpuCollector(
            new ThrowingWmiQueryAdapter(new OperationCanceledException("Native timeout.")),
            SupportedPlatform.Instance);

        CollectionRunResult result = await new InventoryCollectorOrchestrator(2).CollectAsync(
            [denied, timedOut],
            new AgentOptions(),
            "fixture-errors");

        Assert.Collection(
            result.Results,
            item => Assert.Equal(CollectionState.AccessDenied, item.State),
            item => Assert.Equal(CollectionState.TimedOut, item.State));
    }

    [Fact]
    public void WmiQuery_RejectsUnsafeIdentifiersAndSelectsExplicitProperties()
    {
        var valid = new WmiQuery(
            @"\\.\root\cimv2",
            "Win32_OperatingSystem",
            ["Caption", "Version"]);

        Assert.Equal("SELECT Caption, Version FROM Win32_OperatingSystem", valid.ToWql());
        Assert.Throws<ArgumentException>(() => new WmiQuery(
            @"\\.\root\cimv2",
            "Win32_OperatingSystem; DELETE",
            ["Caption"]).ToWql());
        Assert.Throws<ArgumentException>(() => new WmiQuery(
            @"\\.\root\cimv2",
            "Win32_OperatingSystem",
            ["Caption, Password"]).ToWql());
    }

    [Fact]
    public void RegistryValueConverter_HandlesCommonAndMalformedValues()
    {
        Assert.Equal("value", RegistryValueConverter.ToString(" value "));
        Assert.Equal("A;B", RegistryValueConverter.ToString(RegistryMultiString));
        Assert.Equal(42UL, RegistryValueConverter.ToUInt64("42"));
        Assert.Null(RegistryValueConverter.ToUInt64("not-a-number"));
        Assert.True(RegistryValueConverter.ToBoolean(1));
        Assert.False(RegistryValueConverter.ToBoolean("off"));
    }

    [Fact]
    public void RegistryFixtureSerializer_RoundTripsCapturedSnapshots()
    {
        var original = new RegistryKeySnapshot(
            @"SOFTWARE\Example",
            new Dictionary<string, object?>
            {
                ["DisplayName"] = "Example App",
                ["EstimatedSize"] = 2048L,
                ["Enabled"] = 1,
                ["Multi"] = RegistryMultiString,
            });

        string json = RegistryFixtureSerializer.Serialize([original]);
        RegistryKeySnapshot replayed = Assert.Single(RegistryFixtureSerializer.Deserialize(json));

        Assert.Equal("Example App", replayed.GetString("DisplayName"));
        Assert.Equal(2048UL, replayed.GetUInt64("EstimatedSize"));
        Assert.True(replayed.GetBoolean("Enabled"));
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BclAdapters_ReturnBoundedSnapshots()
    {
        HostDataSnapshot host = await new HostDataAdapter().GetAsync(CancellationToken.None);
        IReadOnlyList<NetworkDataSnapshot> network = await new NetworkDataAdapter().GetAsync(CancellationToken.None);
        IReadOnlyList<FileSystemDataSnapshot> fileSystems = await new FileSystemDataAdapter().GetAsync(CancellationToken.None);
        IReadOnlyList<ProcessDataSnapshot> processes = await new ProcessDataAdapter().GetAsync(3, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(host.MachineName));
        Assert.InRange(network.Count, 0, 1024);
        Assert.InRange(fileSystems.Count, 0, 256);
        Assert.InRange(processes.Count, 0, 3);
    }

    [Fact]
    public void ServerCoreFixture_RecordsExpectedDesktopNamespaceOmissions()
    {
        using Fixture fixture = Fixture.Load();
        JsonElement scenario = fixture.Root.GetProperty("serverCoreOmissions");

        Assert.Empty(scenario.GetProperty("Win32_DesktopMonitor").EnumerateArray());
        Assert.Empty(scenario.GetProperty("AntiVirusProduct").EnumerateArray());
    }

    private static CollectorContext Context()
    {
        return new CollectorContext(
            new AgentOptions(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            "fixture-core");
    }

    private static Dictionary<string, IReadOnlyList<WmiRow>> ReadWmi(JsonElement element)
    {
        return element.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => (IReadOnlyList<WmiRow>)property.Value.EnumerateArray().Select(Row).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static HostDataSnapshot ReadHost(JsonElement element)
    {
        return new HostDataSnapshot(
            element.GetProperty("machineName").GetString()!,
            element.GetProperty("domainName").GetString(),
            element.GetProperty("operatingSystemDescription").GetString()!,
            element.GetProperty("operatingSystemArchitecture").GetString()!,
            element.GetProperty("processArchitecture").GetString()!,
            element.GetProperty("bootTimeUtc").GetDateTimeOffset(),
            element.GetProperty("timeZoneId").GetString()!);
    }

    private static WmiRow Row(JsonElement element)
    {
        return new WmiRow(element.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => ConvertElement(property.Value),
            StringComparer.OrdinalIgnoreCase));
    }

    private static RegistryKeySnapshot RegistryRow(JsonElement element)
    {
        return new RegistryKeySnapshot(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            element.EnumerateObject().ToDictionary(
                static property => property.Name,
                static property => ConvertElement(property.Value),
                StringComparer.OrdinalIgnoreCase));
    }

    private static object? ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToArray(),
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Number when element.TryGetInt64(out long number) => number,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            _ => element.GetRawText(),
        };
    }

    private sealed class Fixture : IDisposable
    {
        private readonly JsonDocument _document;

        private Fixture(JsonDocument document)
        {
            _document = document;
        }

        public JsonElement Root => _document.RootElement;

        public static Fixture Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "core-collectors.json");
            return new Fixture(JsonDocument.Parse(File.ReadAllText(path)));
        }

        public void Dispose()
        {
            _document.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class FakeWmiQueryAdapter(
        IReadOnlyDictionary<string, IReadOnlyList<WmiRow>> rows) : IWmiQueryAdapter
    {
        public List<WmiQuery> Queries { get; } = [];

        public ValueTask<IReadOnlyList<WmiRow>> QueryAsync(
            WmiQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(query);
            return ValueTask.FromResult(rows.TryGetValue(query.ClassName, out IReadOnlyList<WmiRow>? result)
                ? result
                : (IReadOnlyList<WmiRow>)[]);
        }
    }

    private sealed class ThrowingWmiQueryAdapter(Exception exception) : IWmiQueryAdapter
    {
        public ValueTask<IReadOnlyList<WmiRow>> QueryAsync(
            WmiQuery query,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromException<IReadOnlyList<WmiRow>>(exception);
        }
    }

    private sealed class FakeRegistryQueryAdapter(RegistryKeySnapshot snapshot) : IRegistryQueryAdapter
    {
        public ValueTask<RegistryKeySnapshot?> ReadKeyAsync(
            RegistryHive hive,
            RegistryView view,
            string path,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<RegistryKeySnapshot?>(snapshot);
        }

        public ValueTask<IReadOnlyList<RegistryKeySnapshot>> EnumerateSubKeysAsync(
            RegistryHive hive,
            RegistryView view,
            string path,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IReadOnlyList<RegistryKeySnapshot>>([]);
        }
    }

    private sealed class FakeHostDataAdapter(HostDataSnapshot snapshot) : IHostDataAdapter
    {
        public ValueTask<HostDataSnapshot> GetAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(snapshot);
        }
    }

    private sealed class SupportedPlatform : IWindowsPlatform
    {
        public static SupportedPlatform Instance { get; } = new();

        public bool IsWindows => true;
    }
}
