using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Bcl;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class VolumeCollector : WindowsCollectorBase
{
    private readonly IFileSystemDataAdapter _fileSystems;

    public VolumeCollector(IFileSystemDataAdapter fileSystems, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _fileSystems = fileSystems;
    }

    public override string Name => "windows-volumes";

    public override InventoryCategory Category => InventoryCategory.Volume;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FileSystemDataSnapshot> fileSystems = await _fileSystems.GetAsync(cancellationToken).ConfigureAwait(false);
        string? systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd(Path.DirectorySeparatorChar);
        return new InventoryContribution
        {
            Source = Name,
            Volumes = Map(fileSystems, systemDrive),
        };
    }

    public static VolumeInfo[] Map(
        IEnumerable<FileSystemDataSnapshot> fileSystems,
        string? systemDrive)
    {
        ArgumentNullException.ThrowIfNull(fileSystems);
        return fileSystems.Select(fileSystem => new VolumeInfo(
                fileSystem.Name,
                fileSystem.Name,
                fileSystem.RootDirectory,
                fileSystem.VolumeLabel,
                fileSystem.Format,
                fileSystem.DriveType,
                fileSystem.TotalBytes,
                fileSystem.FreeBytes,
                IsSystemDrive(fileSystem.Name, systemDrive)))
            .OrderBy(static volume => volume.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSystemDrive(string name, string? systemDrive)
    {
        return systemDrive is not null
            && name.TrimEnd(Path.DirectorySeparatorChar)
                .Equals(systemDrive, StringComparison.OrdinalIgnoreCase);
    }
}
