using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BookDB.MetadataSources.Sources;
using Xunit;

namespace BookDB.Logic.Tests.MetadataSources;

public class GoogleBooksClientTests
{
    private const string ValidGoogleBooksJson = @"{
  ""items"": [
    {
      ""volumeInfo"": {
        ""title"": ""Test Title"",
        ""authors"": [""Author One""],
        ""publisher"": ""Test Pub"",
        ""publishedDate"": ""2020-01-15"",
        ""description"": ""A great book"",
        ""pageCount"": 320,
        ""language"": ""en"",
        ""imageLinks"": {
          ""thumbnail"": ""http://books.google.com/books/content?id=abc&edge=curl&zoom=1&img=1""
        }
      }
    }
  ]
}";

    private static GoogleBooksClient CreateClient(string responseJson)
    {
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new System.Uri("https://www.googleapis.com/books/v1/")
        };
        return new GoogleBooksClient(httpClient);
    }

    [Fact]
    public async Task FetchAsync_ValidIsbn_ReturnsBookMetadata()
    {
        var client = CreateClient(ValidGoogleBooksJson);
        var result = await client.FetchAsync("9780451526342", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test Title", result.Title);
        Assert.Contains("Author One", result.Authors);
        Assert.Equal("Test Pub", result.Publisher);
        Assert.Equal(320, result.Pages);
        Assert.Equal("en", result.Language);
    }

    [Fact]
    public async Task FetchAsync_EmptyItems_ReturnsNull()
    {
        var client = CreateClient(@"{""items"": []}");
        var result = await client.FetchAsync("9780451526342", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchAsync_CoverUrl_StripsEdgeCurlAndUpgradesToHttps()
    {
        var client = CreateClient(ValidGoogleBooksJson);
        var result = await client.FetchAsync("9780451526342", CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.CoverImageUrl);
        Assert.StartsWith("https://", result.CoverImageUrl);
        Assert.DoesNotContain("edge=curl", result.CoverImageUrl);
    }
}
