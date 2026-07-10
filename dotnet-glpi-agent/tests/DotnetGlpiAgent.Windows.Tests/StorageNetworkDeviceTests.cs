using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Bcl;
using DotnetGlpiAgent.Windows.Collectors;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Tests;

public sealed class StorageNetworkDeviceTests
{
    [Fact]
    public void MapDisk_ClassifiesNvmeSsdAndPreservesTypedCapacity()
    {
        WmiRow row = Row(new Dictionary<string, object?>
        {
            ["DeviceID"] = @"\\.\PHYSICALDRIVE0",
            ["Index"] = 0,
            ["Model"] = "Example NVMe SSD",
            ["Manufacturer"] = "Example",
            ["SerialNumber"] = " NVME-SERIAL-1 ",
            ["FirmwareRevision"] = "1.2.3",
            ["Size"] = 1000204886016UL,
            ["InterfaceType"] = "SCSI",
            ["MediaType"] = "Fixed hard disk media",
            ["PNPDeviceID"] = @"SCSI\DISK&VEN_NVME&PROD_EXAMPLE",
        });

        StorageDeviceInfo result = StorageCollector.MapDisk(row);

        Assert.Equal("NVMe SSD", result.MediaType);
        Assert.Equal(1000204886016UL, result.CapacityBytes);
        Assert.Equal("NVME-SERIAL-1", result.SerialNumber);
        Assert.False(result.Removable);
    }

    [Fact]
    public void MapController_UsesStablePnpIdentity()
    {
        WmiRow row = Row(new Dictionary<string, object?>
        {
            ["DeviceID"] = "PCI-CONTROLLER-0",
            ["PNPDeviceID"] = @"PCI\VEN_8086&DEV_2822",
            ["Name"] = "Storage Controller",
            ["Manufacturer"] = "Intel",
            ["Status"] = "OK",
        });

        ControllerInfo result = StorageCollector.MapController(row, "SCSI/Storage");

        Assert.Equal(@"PCI\VEN_8086&DEV_2822", result.Id);
        Assert.Equal("SCSI/Storage", result.Type);
    }

    [Fact]
    public void VolumeMap_EmitsReadyAndAddressableVolumesDeterministically()
    {
        FileSystemDataSnapshot[] snapshots =
        [
            new("D:", @"D:\", "Data", "NTFS", "Fixed", 2000, 500, true),
            new("C:", @"C:\", "System", "NTFS", "Fixed", 1000, 250, true),
            new("E:", @"E:\", null, null, "CDRom", null, null, false),
        ];

        VolumeInfo[] result = VolumeCollector.Map(snapshots, "C:");

        Assert.Collection(
            result,
            item =>
            {
                Assert.Equal("C:", item.Id);
                Assert.True(item.IsSystemVolume);
            },
            item => Assert.Equal("D:", item.Id),
            item =>
            {
                Assert.Equal("E:", item.Id);
                Assert.Null(item.TotalBytes);
            });
    }

    [Fact]
    public void NetworkMap_EmitsOneEntryPerAddressAndAddresslessFallback()
    {
        const string firstId = "4db2772d-97d0-47c3-bfe1-597e4e65bcaf";
        NetworkDataSnapshot[] bcl =
        [
            new(
                firstId,
                "Ethernet",
                "Example Ethernet",
                "001122334455",
                "Ethernet",
                "Up",
                1000000000,
                false,
                true,
                [new NetworkAddressSnapshot("10.0.2.15", 24, "InterNetwork")],
                ["10.0.2.2"],
                ["10.0.2.3"]),
            new(
                "addressless-adapter",
                "Disconnected",
                "Disconnected Adapter",
                "AABBCCDDEEFF",
                "Ethernet",
                "Down",
                null,
                false,
                false,
                [],
                [],
                []),
        ];
        WmiRow[] adapters =
        [
            Row(new Dictionary<string, object?>
            {
                ["GUID"] = $"{{{firstId}}}",
                ["Name"] = "Intel Ethernet",
                ["Description"] = "Intel Adapter",
                ["MACAddress"] = "00:11:22:33:44:55",
                ["AdapterType"] = "Ethernet 802.3",
                ["NetConnectionStatus"] = 2,
                ["PhysicalAdapter"] = true,
                ["Speed"] = 1000000000UL,
            }),
        ];
        WmiRow[] configurations =
        [
            Row(new Dictionary<string, object?>
            {
                ["SettingID"] = firstId,
                ["MACAddress"] = "00-11-22-33-44-55",
                ["DHCPEnabled"] = true,
                ["IPAddress"] = new[] { "10.0.2.15", "fe80::1234" },
                ["IPSubnet"] = new[] { "255.255.255.0", "64" },
                ["DefaultIPGateway"] = new[] { "10.0.2.2" },
                ["DNSServerSearchOrder"] = new[] { "10.0.2.3", "1.1.1.1" },
            }),
        ];

        NetworkAdapterInfo[] result = NetworkCollector.Map(bcl, adapters, configurations);

        Assert.Equal(3, result.Length);
        NetworkAdapterInfo ipv4 = Assert.Single(result, static item => item.Addresses.Any(address => address.Address == "10.0.2.15"));
        Assert.Equal("00:11:22:33:44:55", ipv4.MacAddress);
        Assert.Equal(24, Assert.Single(ipv4.Addresses).PrefixLength);
        Assert.Equal(["1.1.1.1", "10.0.2.3"], ipv4.DnsServers);
        Assert.Contains(result, static item =>
            item.Id.Equals("addressless-adapter", StringComparison.OrdinalIgnoreCase)
            && item.Addresses.Count == 0);
    }

    [Fact]
    public void DeviceMap_ParsesUsbIdentityAndSuppressesHubs()
    {
        WmiRow[] rows =
        [
            Row(new Dictionary<string, object?>
            {
                ["DeviceID"] = @"USB\VID_046D&PID_C534\ABC123",
                ["Name"] = "USB Receiver",
                ["Manufacturer"] = "Logitech",
                ["PNPClass"] = "USB",
                ["Present"] = true,
                ["Service"] = "usbccgp",
                ["Status"] = "OK",
            }),
            Row(new Dictionary<string, object?>
            {
                ["DeviceID"] = @"USB\ROOT_HUB30\4&123",
                ["Name"] = "USB Root Hub",
                ["PNPClass"] = "USB",
                ["Present"] = true,
                ["Service"] = "USBHUB3",
                ["Status"] = "OK",
            }),
            Row(new Dictionary<string, object?>
            {
                ["DeviceID"] = @"USB\VID_DEAD&PID_BEEF\REMOVED",
                ["Name"] = "Removed Device",
                ["Present"] = false,
            }),
        ];

        (UsbDeviceInfo[] usb, PnpDeviceInfo[] pnp) = DeviceCollector.Map(rows);

        UsbDeviceInfo device = Assert.Single(usb);
        Assert.Equal("046D", device.VendorId);
        Assert.Equal("C534", device.ProductId);
        Assert.Equal("ABC123", device.SerialNumber);
        Assert.Equal(2, pnp.Length);
    }

    private static WmiRow Row(IReadOnlyDictionary<string, object?> values) => new(values);
}
