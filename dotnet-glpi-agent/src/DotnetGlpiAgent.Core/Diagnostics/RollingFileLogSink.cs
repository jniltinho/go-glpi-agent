using System.Text;
using System.Text.Json;

namespace DotnetGlpiAgent.Core.Diagnostics;

public interface IFilePermissionHardener
{
    void HardenDirectory(string path);

    void HardenFile(string path);
}

public sealed class RollingFileLogSink : IAgentLogSink, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _directory;
    private readonly string _path;
    private readonly long _maximumBytes;
    private readonly int _retainedFiles;
    private readonly IFilePermissionHardener _permissionHardener;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public RollingFileLogSink(
        string directory,
        string fileName,
        long maximumBytes,
        int retainedFiles,
        IFilePermissionHardener permissionHardener)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedFiles, 1);

        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new ArgumentException("The log file name must not contain a path.", nameof(fileName));
        }

        _directory = Path.GetFullPath(directory);
        _path = Path.Combine(_directory, fileName);
        _maximumBytes = maximumBytes;
        _retainedFiles = retainedFiles;
        _permissionHardener = permissionHardener ?? throw new ArgumentNullException(nameof(permissionHardener));

        Directory.CreateDirectory(_directory);
        _permissionHardener.HardenDirectory(_directory);
    }

    public async ValueTask WriteAsync(AgentLogEntry entry, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, SerializerOptions) + "\n");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length + data.Length > _maximumBytes)
            {
                Rotate();
            }

            await using var stream = new FileStream(
                _path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            _permissionHardener.HardenFile(_path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void Rotate()
    {
        File.Delete(ArchivePath(_retainedFiles));
        for (int index = _retainedFiles - 1; index >= 1; index--)
        {
            string source = ArchivePath(index);
            if (File.Exists(source))
            {
                File.Move(source, ArchivePath(index + 1));
            }
        }

        if (File.Exists(_path))
        {
            File.Move(_path, ArchivePath(1));
        }
    }

    private string ArchivePath(int index) => $"{_path}.{index}";
}
