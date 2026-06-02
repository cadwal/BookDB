using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.MetadataSources.Sources;
using Xunit;

namespace BookDB.Logic.Tests.MetadataSources;

// Libris Xsearch Dublin Core JSON (confirmed via live API spike on 2026-03-24)
public class LibrisKbClientTests
{
    private const string ValidLibrisXsearchJson = @"{
  ""xsearch"": {
    ""from"": 1,
    ""to"": 1,
    ""records"": 1,
    ""list"": [
      {
        ""identifier"": ""http://libris.kb.se/bib/14744529"",
        ""title"": ""Anteckningar fran en kolchos"",
        ""creator"": ""Rausing, Sigrid, 1962-"",
        ""isbn"": ""9789100137403"",
        ""type"": ""book"",
        ""publisher"": ""Stockholm : Bonnier"",
        ""date"": ""2014"",
        ""language"": ""swe""
      }
    ]
  }
}";

    private static LibrisKbClient CreateClient(string responseJson)
    {
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new System.Uri("https://libris.kb.se/")
        };
        return new LibrisKbClient(httpClient);
    }

    [Fact]
    public async Task FetchAsync_ValidIsbn_ReturnsBookMetadataWithTitleAndAuthors()
    {
        var client = CreateClient(ValidLibrisXsearchJson);
        var result = await client.FetchAsync("9789100137403", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Anteckningar fran en kolchos", result.Title);
        Assert.NotEmpty(result.Authors);
    }

    [Fact]
    public async Task FetchAsync_EmptyItems_ReturnsNull()
    {
        var emptyJson = @"{""xsearch"": {""from"": 1, ""to"": 0, ""records"": 0, ""list"": []}}";
        var client = CreateClient(emptyJson);
        var result = await client.FetchAsync("9789100137403", CancellationToken.None);
        Assert.Null(result);
    }
}
