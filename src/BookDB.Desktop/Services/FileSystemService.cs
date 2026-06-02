using System.IO;
using System.Threading.Tasks;
using System.Threading;
using BookDB.Models.Interfaces;

namespace BookDB.Desktop.Services;

public sealed class FileSystemService : IFileSystemService
{
    public void EnsureDirectoryExists(string path)
        => Directory.CreateDirectory(path);

    public string CombinePath(params string[] parts)
        => Path.Combine(parts);

    public bool FileExists(string path)
        => File.Exists(path);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    public async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        await using var sourceStream = File.OpenRead(source);
        await using var destStream = File.Create(destination);
        await sourceStream.CopyToAsync(destStream, cancellationToken);
    }
}
