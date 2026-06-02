using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.MetadataSources.Sources;
using Xunit;

namespace BookDB.Logic.Tests.MetadataSources;

public class OpenLibraryClientTests
{
    private const string ValidOpenLibraryJson = @"{
  ""ISBN:9789100137403"": {
    ""title"": ""Anteckningar fran en kolchos"",
    ""authors"": [{""name"": ""Sigrid Rausing""}],
    ""publishers"": [{""name"": ""Bonnier""}],
    ""publish_date"": ""2014"",
    ""languages"": [{""key"": ""/languages/swe""}],
    ""number_of_pages"": 256,
    ""cover"": {
      ""medium"": ""https://covers.openlibrary.org/b/id/123456-M.jpg""
    }
  }
}";

    private static OpenLibraryClient CreateClient(string responseJson)
    {
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new System.Uri("https://openlibrary.org/")
        };
        return new OpenLibraryClient(httpClient);
    }

    [Fact]
    public async Task FetchAsync_ValidIsbn_ReturnsBookMetadata()
    {
        var client = CreateClient(ValidOpenLibraryJson);
        var result = await client.FetchAsync("9789100137403", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Anteckningar fran en kolchos", result.Title);
        Assert.Contains("Sigrid Rausing", result.Authors);
        Assert.Equal("Bonnier", result.Publisher);
        Assert.Equal(256, result.Pages);
    }

    [Fact]
    public async Task FetchAsync_SweLanguage_MapsTwoLetterCode()
    {
        var client = CreateClient(ValidOpenLibraryJson);
        var result = await client.FetchAsync("9789100137403", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("sv", result.Language);
    }

    [Fact]
    public async Task FetchAsync_EmptyDictionary_ReturnsNull()
    {
        var client = CreateClient("{}");
        var result = await client.FetchAsync("9789100137403", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchAsync_CoverUrl_ReturnsFromCoverMedium()
    {
        var client = CreateClient(ValidOpenLibraryJson);
        var result = await client.FetchAsync("9789100137403", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("https://covers.openlibrary.org/b/id/123456-M.jpg", result.CoverImageUrl);
    }

    [Theory]
    [InlineData("/languages/eng", "en")]
    [InlineData("/languages/swe", "sv")]
    [InlineData("/languages/fre", "fr")]
    [InlineData("/languages/ger", "de")]
    public void LanguageParsing_MapsCorrectly(string languageKey, string expected)
    {
        var actual = OpenLibraryClient.MapLanguageCode(languageKey.Split('/')[^1]);
        Assert.Equal(expected, actual);
    }
}
