using System.Globalization;
using System.Text;
using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Collectors;
using DotnetGlpiAgent.Windows.Management;
using DotnetGlpiAgent.Windows.Monitors;

namespace DotnetGlpiAgent.Windows.Tests;

public sealed class ExtendedCollectorTests
{
    [Fact]
    public void PrinterMap_MapsStatusAndConnectionFlags()
    {
        PrinterInfo printer = Assert.Single(PrinterCollector.Map(
        [
            Row(
                ("DeviceID", "Printer-1"),
                ("Name", "Office Printer"),
                ("DriverName", "Universal Driver"),
                ("PortName", "10.0.0.20"),
                ("PrinterStatus", 3),
                ("Default", true),
                ("Network", true),
                ("Shared", false)),
        ]));

        Assert.Equal("Idle", printer.Status);
        Assert.True(printer.IsDefault);
        Assert.True(printer.IsNetwork);
        Assert.False(printer.IsShared);
    }

    [Fact]
    public void PeripheralMaps_UseStablePnpIdentityAndTypedValues()
    {
        WmiRow videoRow = Row(
            ("PNPDeviceID", @"PCI\VEN_1234&DEV_1111"),
            ("Name", "Example Display"),
            ("AdapterCompatibility", "Example"),
            ("DriverVersion", "1.2.3"),
            ("AdapterRAM", 4294967296UL),
            ("CurrentHorizontalResolution", 1920),
            ("CurrentVerticalResolution", 1080),
            ("Status", "OK"));

        VideoAdapterInfo video = PeripheralCollector.MapVideo(videoRow)!;
        SoundDeviceInfo sound = PeripheralCollector.MapSound(Row(("PNPDeviceID", "SOUND-1"), ("Name", "Audio")))!;
        InputDeviceInfo input = PeripheralCollector.MapInput(Row(("DeviceID", "KEYBOARD-1")), "Keyboard")!;
        PortInfo modem = PeripheralCollector.MapPort(Row(("DeviceID", "MODEM-1"), ("Name", "Modem")), "Modem")!;

        Assert.Equal("1920x1080", video.Resolution);
        Assert.Equal(4294967296UL, video.MemoryBytes);
        Assert.Equal("SOUND-1", sound.Id);
        Assert.Equal("Keyboard", input.Type);
        Assert.Equal("Modem", modem.Type);
    }

    [Fact]
    public void BatteryMap_MapsCapacityChemistryAndStatus()
    {
        BatteryInfo battery = BatteryCollector.Map(Row(
            ("DeviceID", "BAT0"),
            ("Name", "Internal Battery"),
            ("Manufacturer", "Example"),
            ("Chemistry", 6),
            ("DesignCapacity", 60000UL),
            ("FullChargeCapacity", 57000UL),
            ("EstimatedChargeRemaining", 75),
            ("DesignVoltage", 11400),
            ("BatteryStatus", 6)))!;

        Assert.Equal("Lithium-ion", battery.Chemistry);
        Assert.Equal(57000UL, battery.FullChargeCapacityMilliwattHours);
        Assert.Equal(75U, battery.ChargePercent);
        Assert.Equal("Charging", battery.Status);
    }

    [Fact]
    public void EdidParser_MapsIdentityDescriptorResolutionAndDate()
    {
        byte[] edid = CreateEdid();

        MonitorDataSnapshot monitor = EdidParser.Parse("DISPLAY\\ABC1234\\1", edid)!;

        Assert.Equal("ABC", monitor.Manufacturer);
        Assert.Equal("Example Panel", monitor.Model);
        Assert.Equal("SERIAL-EDID", monitor.SerialNumber);
        Assert.Equal(1920U, monitor.HorizontalPixels);
        Assert.Equal(1080U, monitor.VerticalPixels);
        Assert.Equal(2026, monitor.ManufactureDate?.Year);
    }

    [Fact]
    public void MonitorMap_DecodesWmiCharacterArrays()
    {
        MonitorInfo monitor = Assert.Single(MonitorCollector.MapWmi(
        [
            Row(
                ("Active", true),
                ("InstanceName", "DISPLAY\\ABC1234\\1"),
                ("ManufacturerName", Chars("ABC")),
                ("ProductCodeID", Chars("Panel")),
                ("SerialNumberID", Chars("SERIAL")),
                ("WeekOfManufacture", 10),
                ("YearOfManufacture", 2026)),
        ]));

        Assert.Equal("ABC", monitor.Manufacturer);
        Assert.Equal("Panel", monitor.Model);
        Assert.Equal(2026, monitor.ManufactureDate?.Year);
    }

    [Fact]
    public void AntivirusMaps_SecurityCenterAndDefenderState()
    {
        AntivirusInfo securityCenter = AntivirusCollector.MapSecurityCenter(Row(
            ("displayName", "Example AV"),
            ("instanceGuid", "{ef840f88-0016-45bd-9c35-8c32f2809847}"),
            ("productState", 0x1010)))!;
        AntivirusInfo defender = AntivirusCollector.MapDefender(Row(
            ("AMServiceEnabled", true),
            ("AntivirusEnabled", true),
            ("RealTimeProtectionEnabled", true),
            ("AntivirusSignatureAge", 1),
            ("AntivirusSignatureVersion", "1.2.3")))!;

        Assert.True(securityCenter.Enabled);
        Assert.False(securityCenter.UpToDate);
        Assert.True(defender.Enabled);
        Assert.True(defender.UpToDate);
        Assert.Equal("1.2.3", defender.Version);
    }

    [Fact]
    public void FirewallMap_MapsProfilesAndDefaultActions()
    {
        FirewallProfileInfo profile = Assert.Single(FirewallCollector.Map(
        [
            Row(
                ("Name", "Domain"),
                ("Enabled", true),
                ("DefaultInboundAction", 2),
                ("DefaultOutboundAction", 1)),
        ]));

        Assert.True(profile.Enabled);
        Assert.Equal("Block", profile.DefaultInboundAction);
        Assert.Equal("Allow", profile.DefaultOutboundAction);
    }

    [Fact]
    public async Task DesktopOnlySources_UnavailableOnServerCore_DoNotFailContribution()
    {
        var wmi = new UnavailableWmiAdapter();
        var antivirus = new AntivirusCollector(wmi, SupportedPlatform.Instance);
        var firewall = new FirewallCollector(wmi, registry: null, SupportedPlatform.Instance);
        var monitor = new MonitorCollector(wmi, new EmptyEdidAdapter(), SupportedPlatform.Instance);

        InventoryContribution antivirusResult = await antivirus.CollectAsync(Context(), CancellationToken.None);
        InventoryContribution firewallResult = await firewall.CollectAsync(Context(), CancellationToken.None);
        InventoryContribution monitorResult = await monitor.CollectAsync(Context(), CancellationToken.None);

        Assert.Empty(antivirusResult.AntivirusProducts);
        Assert.NotEmpty(antivirusResult.Diagnostics);
        Assert.Empty(firewallResult.FirewallProfiles);
        Assert.Contains(firewallResult.Diagnostics, static d => d.State == CollectionState.Unavailable);
        Assert.Empty(monitorResult.Monitors);
        Assert.Equal(CollectionState.Unavailable, Assert.Single(monitorResult.Diagnostics).State);
    }

    private static byte[] CreateEdid()
    {
        var edid = new byte[128];
        new byte[] { 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 }.CopyTo(edid, 0);
        int manufacturer = ((('A' - 64) & 0x1f) << 10) | ((('B' - 64) & 0x1f) << 5) | (('C' - 64) & 0x1f);
        edid[8] = (byte)(manufacturer >> 8);
        edid[9] = (byte)manufacturer;
        edid[16] = 10;
        edid[17] = 36;
        WriteDetailedTiming(edid, 54, 1920, 1080);
        WriteDescriptor(edid, 72, 0xfc, "Example Panel");
        WriteDescriptor(edid, 90, 0xff, "SERIAL-EDID");
        return edid;
    }

    private static void WriteDetailedTiming(byte[] edid, int offset, int width, int height)
    {
        edid[offset] = 1;
        edid[offset + 2] = (byte)width;
        edid[offset + 4] = (byte)((width >> 8) << 4);
        edid[offset + 5] = (byte)height;
        edid[offset + 7] = (byte)((height >> 8) << 4);
    }

    private static void WriteDescriptor(byte[] edid, int offset, byte type, string value)
    {
        edid[offset + 3] = type;
        byte[] text = Encoding.ASCII.GetBytes(value.PadRight(13)[..13]);
        text.CopyTo(edid, offset + 5);
    }

    private static ushort[] Chars(string value) => [.. value.Select(static character => (ushort)character), 0];

    private static WmiRow Row(params (string Key, object? Value)[] values)
    {
        return new WmiRow(values.ToDictionary(static pair => pair.Key, static pair => pair.Value));
    }

    private static CollectorContext Context()
    {
        return new CollectorContext(new AgentOptions(), DateTimeOffset.UtcNow.AddMinutes(1), "extended-fixture");
    }

    private sealed class UnavailableWmiAdapter : IWmiQueryAdapter
    {
        public ValueTask<IReadOnlyList<WmiRow>> QueryAsync(WmiQuery query, CancellationToken cancellationToken)
        {
            throw new CollectorFailureException(CollectionState.Unavailable, "wmi-source-unavailable", "Source is absent.");
        }
    }

    private sealed class EmptyEdidAdapter : IEdidRegistryAdapter
    {
        public ValueTask<IReadOnlyList<MonitorDataSnapshot>> EnumerateAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IReadOnlyList<MonitorDataSnapshot>>([]);
        }
    }

    private sealed class SupportedPlatform : IWindowsPlatform
    {
        public static SupportedPlatform Instance { get; } = new();

        public bool IsWindows => true;
    }
}
