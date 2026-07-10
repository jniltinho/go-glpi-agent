namespace DotnetGlpiAgent.Windows.Bcl;

public sealed record FileSystemDataSnapshot(
    string Name,
    string? RootDirectory,
    string? VolumeLabel,
    string? Format,
    string DriveType,
    ulong? TotalBytes,
    ulong? FreeBytes,
    bool IsReady);

public interface IFileSystemDataAdapter
{
    ValueTask<IReadOnlyList<FileSystemDataSnapshot>> GetAsync(CancellationToken cancellationToken);
}

public sealed class FileSystemDataAdapter : IFileSystemDataAdapter
{
    private const int MaximumDrives = 256;

    public ValueTask<IReadOnlyList<FileSystemDataSnapshot>> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemDataSnapshot[] snapshots = DriveInfo.GetDrives()
            .OrderBy(static drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumDrives)
            .Select(drive => Capture(drive, cancellationToken))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<FileSystemDataSnapshot>>(snapshots);
    }

    private static FileSystemDataSnapshot Capture(DriveInfo drive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!drive.IsReady)
        {
            return new FileSystemDataSnapshot(
                drive.Name,
                drive.RootDirectory.FullName,
                null,
                null,
                drive.DriveType.ToString(),
                null,
                null,
                false);
        }

        return new FileSystemDataSnapshot(
            drive.Name,
            drive.RootDirectory.FullName,
            SafeGet(() => drive.VolumeLabel),
            SafeGet(() => drive.DriveFormat),
            drive.DriveType.ToString(),
            ToUnsigned(SafeGet(() => drive.TotalSize)),
            ToUnsigned(SafeGet(() => drive.AvailableFreeSpace)),
            true);
    }

    private static T? SafeGet<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch (IOException)
        {
            return default;
        }
        catch (UnauthorizedAccessException)
        {
            return default;
        }
    }

    private static ulong? ToUnsigned(long? value) => value is >= 0 ? (ulong)value.Value : null;
}
