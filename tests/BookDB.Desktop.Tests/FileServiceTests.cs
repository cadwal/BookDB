using System.IO;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using Xunit;

namespace BookDB.Desktop.Tests;

public class FileServiceTests
{
    [Fact]
    public void FileSystemService_CombinePath_JoinsTwoParts()
    {
        var svc = new FileSystemService();
        var sep = Path.DirectorySeparatorChar;

        var result = svc.CombinePath("a", "b");

        Assert.Equal($"a{sep}b", result);
    }

    [Fact]
    public void FileSystemService_EnsureDirectoryExists_CreatesDirectory()
    {
        var svc = new FileSystemService();
        var tempDir = Path.Combine(Path.GetTempPath(), $"bookdb_test_{System.Guid.NewGuid():N}");

        try
        {
            svc.EnsureDirectoryExists(tempDir);
            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemService_CopyFileAsync_CopiesContent()
    {
        var svc = new FileSystemService();
        var sourceFile = Path.GetTempFileName();
        var destFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(sourceFile, "hello bookdb", TestContext.Current.CancellationToken);
            await svc.CopyFileAsync(sourceFile, destFile, TestContext.Current.CancellationToken);
            var content = await File.ReadAllTextAsync(destFile, TestContext.Current.CancellationToken);
            Assert.Equal("hello bookdb", content);
        }
        finally
        {
            File.Delete(sourceFile);
            File.Delete(destFile);
        }
    }
}
