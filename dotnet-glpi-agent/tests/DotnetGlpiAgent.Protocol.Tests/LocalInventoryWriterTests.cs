using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Serialization;

namespace DotnetGlpiAgent.Protocol.Tests;

public sealed class LocalInventoryWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesJsonAndXmlAtomicallyFromOneSnapshot()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dotnet-glpi-local-{Guid.NewGuid():N}");
        try
        {
            InventorySnapshot snapshot = new()
            {
                Identity = new AgentIdentity(
                    Guid.Parse("3c33b875-71b4-4016-a95a-e62d012c4e5b"),
                    "HOST-2026-07-10-12-00-00",
                    "HOST",
                    "Dotnet-GLPI-Agent/0.1.0"),
                Collection = new CollectionMetadata(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, true, [], "local-test"),
            };
            LocalInventoryFiles first = await LocalInventoryWriter.WriteAsync(snapshot, directory, CancellationToken.None);
            byte[] firstJson = await File.ReadAllBytesAsync(first.JsonPath);
            LocalInventoryFiles second = await LocalInventoryWriter.WriteAsync(snapshot, directory, CancellationToken.None);

            Assert.Equal(first, second);
            Assert.Equal(firstJson, await File.ReadAllBytesAsync(second.JsonPath));
            Assert.Contains("\"deviceid\":\"HOST-2026-07-10-12-00-00\"", await File.ReadAllTextAsync(first.JsonPath), StringComparison.Ordinal);
            Assert.Contains("<DEVICEID>HOST-2026-07-10-12-00-00</DEVICEID>", await File.ReadAllTextAsync(first.XmlPath), StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
