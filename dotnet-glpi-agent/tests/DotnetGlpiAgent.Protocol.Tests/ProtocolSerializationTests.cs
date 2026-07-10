using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Serialization;

namespace DotnetGlpiAgent.Protocol.Tests;

public sealed class ProtocolSerializationTests
{
    [Fact]
    public void SerializeInventory_MatchesCanonicalGoldenJson()
    {
        InventorySnapshot snapshot = CreateSnapshot();
        byte[] actual = NativeJsonSerializer.SerializeInventory(snapshot);
        byte[] repeated = NativeJsonSerializer.SerializeInventory(snapshot);
        byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "native-inventory.json"));

        Assert.Equal(expected.AsSpan().TrimEnd((byte)'\n').ToArray(), actual);
        Assert.Equal(actual, repeated);

        using JsonDocument document = JsonDocument.Parse(actual);
        JsonElement root = document.RootElement;
        Assert.Equal("inventory", root.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("content").GetProperty("cpus")[0].GetProperty("core").ValueKind);
        Assert.Equal(JsonValueKind.False, root.GetProperty("content").GetProperty("networks")[0].GetProperty("virtualdev").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("content").GetProperty("softwares")[0].GetProperty("filesize").ValueKind);
    }

    [Fact]
    public void JsonAndXml_ConsumeTheSameMappedSnapshot()
    {
        InventorySnapshot snapshot = CreateSnapshot();
        using JsonDocument json = JsonDocument.Parse(NativeJsonSerializer.SerializeInventory(snapshot));
        XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(LegacyXmlSerializer.SerializeInventory(snapshot)));

        JsonElement content = json.RootElement.GetProperty("content");
        XElement xmlContent = xml.Root!.Element("CONTENT")!;
        Assert.Equal(
            content.GetProperty("hardware").GetProperty("name").GetString(),
            xmlContent.Element("HARDWARE")!.Element("NAME")!.Value);
        Assert.Equal(
            content.GetProperty("softwares")[0].GetProperty("name").GetString(),
            xmlContent.Element("SOFTWARES")!.Element("NAME")!.Value);
        Assert.Equal("INVENTORY", xml.Root.Element("QUERY")!.Value);
        Assert.Equal(snapshot.Identity.DeviceId, xml.Root.Element("DEVICEID")!.Value);
    }

    [Fact]
    public void ContactAndProlog_ContainPersistentIdentityAndInventoryTask()
    {
        InventorySnapshot snapshot = CreateSnapshot();
        using JsonDocument contact = JsonDocument.Parse(NativeJsonSerializer.SerializeContact(snapshot));
        XDocument prolog = XDocument.Parse(Encoding.UTF8.GetString(LegacyXmlSerializer.SerializeProlog(snapshot.Identity.DeviceId)));

        Assert.Equal(snapshot.Identity.DeviceId, contact.RootElement.GetProperty("deviceid").GetString());
        Assert.Equal("contact", contact.RootElement.GetProperty("action").GetString());
        Assert.Equal("inventory", contact.RootElement.GetProperty("installed-tasks")[0].GetString());
        Assert.Equal("PROLOG", prolog.Root!.Element("QUERY")!.Value);
    }

    [Fact]
    public void XmlSerializer_StripsIllegalControlCharacters()
    {
        string dirty = "hello\u001Fworld\u0000!";
        string clean = LegacyXmlSerializer.SanitizeXmlText(dirty);
        Assert.Equal("helloworld!", clean);

        InventorySnapshot snapshot = CreateSnapshot() with
        {
            OperatingSystem = CreateSnapshot().OperatingSystem! with
            {
                Name = "Windows\u001FServer",
            },
        };
        byte[] xml = LegacyXmlSerializer.SerializeInventory(snapshot);
        XDocument document = XDocument.Parse(Encoding.UTF8.GetString(xml));
        Assert.Contains("WindowsServer", document.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"status\":\"ok\",\"tasks\":[\"inventory\"]}", true)]
    [InlineData("{\"status\":\"ok\",\"tasks\":[\"deploy\"]}", false)]
    [InlineData("{\"status\":\"ok\"}", true)]
    public void ParseContactResponse_ValidatesInventoryIntent(string response, bool expected)
    {
        ContactResponse result = NativeJsonSerializer.ParseContactResponse(Encoding.UTF8.GetBytes(response));

        Assert.Equal(expected, result.RequestsInventory());
    }

    private static InventorySnapshot CreateSnapshot()
    {
        return new InventorySnapshot
        {
            Identity = new AgentIdentity(
                Guid.Parse("3c33b875-71b4-4016-a95a-e62d012c4e5b"),
                "WINDOWS-2026-07-10-12-00-00",
                "host",
                "Dotnet-GLPI-Agent/0.1.0"),
            Account = new InventoryAccount("lab"),
            Collection = new CollectionMetadata(
                new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 10, 12, 0, 1, TimeSpan.Zero),
                true,
                [],
                "protocol-fixture"),
            OperatingSystem = new OperatingSystemInfo(
                "Windows Server 2022",
                "Datacenter",
                "10.0",
                "20348",
                "21H2",
                "20348.1",
                "x86_64",
                "host",
                "example.local",
                new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                "E. South America Standard Time"),
            Hardware = new HardwareInfo(
                "Example Corp",
                "Example Model",
                "SYSTEM-SERIAL",
                "7d05c6a8-8872-4cc9-b41f-07c72364cf4c",
                "ASSET-1",
                "Desktop",
                "Board Corp",
                "Board 1",
                "BOARD-SERIAL",
                8UL * 1024 * 1024 * 1024,
                2UL * 1024 * 1024 * 1024),
            Bios = new BiosInfo(
                "Firmware Corp",
                "Example BIOS",
                "1.2.3",
                "BIOS-SERIAL",
                new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                "3.4",
                true),
            Cpus =
            [
                new CpuInfo("cpu-0", "Example CPU", "Example Corp", "x86_64", "CPU0", 4, 8, 3200, 3600, null),
            ],
            Volumes =
            [
                new VolumeInfo("C:", "System", "C:\\", "System", "NTFS", "Fixed", 1024UL * 1024 * 1024, 512UL * 1024 * 1024, true),
            ],
            NetworkAdapters =
            [
                new NetworkAdapterInfo(
                    "ethernet-0",
                    "Ethernet",
                    "Ethernet Adapter",
                    "00:11:22:33:44:55",
                    "Ethernet",
                    "Up",
                    1_000_000_000,
                    false,
                    true,
                    [new NetworkAddressInfo("192.0.2.10", 24, "InterNetwork", true)],
                    ["192.0.2.1"],
                    ["192.0.2.53"]),
            ],
            Software =
            [
                new SoftwareInfo(
                    "software-1",
                    "Example App",
                    "1.0",
                    "Example Corp",
                    "x86_64",
                    new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                    1024 * 1024,
                    "machine-registry",
                    null,
                    "https://example.invalid/app",
                    "uninstall.exe",
                    false,
                    false),
            ],
        };
    }
}
