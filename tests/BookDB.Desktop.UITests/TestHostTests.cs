using System.Threading.Tasks;
using BookDB.Logic.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>Exercises the per-test host: data seeded through the Logic write paths round-trips, and a per-test
/// override replaces a registered service so flows can fake their edges.</summary>
public class TestHostTests
{
    [Fact]
    public async Task SeededBook_RoundTripsThroughTheHost()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = TestHost.Create();

        var seeded = await SeedData.AddBookAsync(host, "Mistborn", ct);
        var readBack = await host.Resolve<IBookService>().GetBookByIdAsync(seeded.BookId, ct);

        Assert.NotNull(readBack);
        Assert.Equal("Mistborn", readBack!.Title);
    }

    [Fact]
    public void Override_ReplacesTheRegisteredService()
    {
        var fake = Substitute.For<IPrintService>();

        using var host = TestHost.Create(services => services.AddSingleton(fake));

        Assert.Same(fake, host.Resolve<IPrintService>());
    }
}
