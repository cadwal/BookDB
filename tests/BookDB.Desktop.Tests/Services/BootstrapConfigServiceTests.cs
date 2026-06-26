using System;
using System.IO;
using BookDB.Desktop.Services;
using Xunit;

namespace BookDB.Desktop.Tests.Services;

public sealed class BootstrapConfigServiceTests
{
    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"bootstrap-svc-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsWithoutCreatingFile()
    {
        var path = TempPath();
        var service = new BootstrapConfigService(path);

        var config = service.Load();

        Assert.Equal("Sqlite", config.Backend);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Update_PersistsAndDoesNotClobberPreviousFields()
    {
        var path = TempPath();
        var service = new BootstrapConfigService(path);
        try
        {
            service.Update(c => c.Language = "sv");
            service.Update(c => c.UiTheme = "Vibrant");

            var reloaded = service.Load();
            Assert.Equal("sv", reloaded.Language);
            Assert.Equal("Vibrant", reloaded.UiTheme);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
