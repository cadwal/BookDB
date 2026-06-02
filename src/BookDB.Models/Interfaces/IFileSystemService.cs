using System.Threading.Tasks;
using System.Threading;

namespace BookDB.Models.Interfaces;

public interface IFileSystemService
{
    void EnsureDirectoryExists(string path);
    string CombinePath(params string[] parts);
    bool FileExists(string path);
    void DeleteFile(string path);
    Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken = default);
}
