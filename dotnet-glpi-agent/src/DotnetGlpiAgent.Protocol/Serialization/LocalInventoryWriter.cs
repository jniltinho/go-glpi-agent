using System.Security.Cryptography;
using DotnetGlpiAgent.Core.Inventory;

namespace DotnetGlpiAgent.Protocol.Serialization;

public sealed record LocalInventoryFiles(string JsonPath, string XmlPath);

public static class LocalInventoryWriter
{
    public static async ValueTask<LocalInventoryFiles> WriteAsync(
        InventorySnapshot snapshot,
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);
        string jsonPath = Path.Combine(fullDirectory, $"{snapshot.Identity.DeviceId}.json");
        string xmlPath = Path.Combine(fullDirectory, $"{snapshot.Identity.DeviceId}.xml");

        await WriteAtomicAsync(
            jsonPath,
            NativeJsonSerializer.SerializeInventory(snapshot),
            cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(
            xmlPath,
            LegacyXmlSerializer.SerializeInventory(snapshot),
            cancellationToken).ConfigureAwait(false);
        return new LocalInventoryFiles(jsonPath, xmlPath);
    }

    private static async ValueTask WriteAtomicAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string temporaryPath = $"{path}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
