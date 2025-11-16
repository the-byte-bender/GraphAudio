using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GraphAudio.Kit.DataProviders;

/// <summary>
/// Provides audio file streams from the local file system.
/// </summary>
public sealed class FileSystemDataProvider : IDataProvider
{
    private readonly string _basePath;

    public FileSystemDataProvider(string basePath)
    {
        if (!Directory.Exists(basePath))
        {
            throw new DirectoryNotFoundException($"The base path '{basePath}' does not exist.");
        }
        _basePath = Path.GetFullPath(basePath);
    }

    /// <inheritdoc/>
    public ValueTask<Stream> GetStreamAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, path));

        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Access to the path is denied.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The file at path '{path}' was not found.", fullPath);
        }

        Stream stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ValueTask.FromResult(stream);
    }
}
