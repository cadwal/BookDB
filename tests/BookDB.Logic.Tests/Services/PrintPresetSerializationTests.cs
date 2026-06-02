using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class PrintPresetSerializationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly ISettingsService _settingsService;

    public PrintPresetSerializationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_presetser_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDbContext))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _factory = new TestBookDbContextFactory(options);
        var lookupService = new LookupService(_factory, new NullResourceProvider());
        _settingsService = lookupService;
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public void PrintPreset_SerializeDeserialize_RoundTripPreservesAllFields()
    {
        var preset = new PrintPreset(
            Name: "MyReport",
            Columns: new[] { "Title", "Authors" },
            Orientation: PageOrientation.Landscape,
            FontSize: 11,
            MarginHorizontalMm: 20,
            MarginVerticalMm: 25,
            HeaderText: "My Header",
            FooterText: "My Footer");

        var json = JsonSerializer.Serialize(new List<PrintPreset> { preset });
        var result = JsonSerializer.Deserialize<List<PrintPreset>>(json);

        Assert.NotNull(result);
        Assert.Single(result);
        var deserialized = result[0];
        Assert.Equal("MyReport", deserialized.Name);
        Assert.Equal(new[] { "Title", "Authors" }, deserialized.Columns);
        Assert.Equal(PageOrientation.Landscape, deserialized.Orientation);
        Assert.Equal(11f, deserialized.FontSize);
        Assert.Equal(20f, deserialized.MarginHorizontalMm);
        Assert.Equal(25f, deserialized.MarginVerticalMm);
        Assert.Equal("My Header", deserialized.HeaderText);
        Assert.Equal("My Footer", deserialized.FooterText);
    }

    [Fact]
    public async Task SettingsService_GetAsync_MissingPrintPresetsKey_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _settingsService.GetAsync("PrintPresets", ct);
        Assert.Null(result);
    }
}
