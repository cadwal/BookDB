using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.MetadataSources.Sources;
using Xunit;

namespace BookDB.Logic.Tests.MetadataSources;

public class IsbnSearchOrgClientTests
{
    private const string ValidIsbnSearchOrgHtml = @"
<!DOCTYPE html>
<html lang=""en"">
  <body>
    <div id=""book"">
      <div class=""image"">
        <img src=""https://images.isbndb.com/covers/20614433482348.jpg"" alt=""1984"">
      </div>
      <div class=""bookinfo"">
        <h1>1984</h1>
        <p><strong>ISBN-13:</strong> <a href=""/isbn/9780451524935"">9780451524935</a></p>
        <p><strong>ISBN-10:</strong> <a href=""/isbn/0451524934"">0451524934</a></p>
        <p><strong>Author:</strong> George Orwell</p>
        <p><strong>Edition:</strong> Large type / Large print</p>
        <p><strong>Binding:</strong> Mass Market Paperback</p>
        <p><strong>Publisher:</strong> Penguin Publishing Group</p>
        <p><strong>Published:</strong> 1950-07-01</p>
      </div>
    </div>
  </body>
</html>";

    private const string BookNotFoundHtml = @"
<!DOCTYPE html>
<html>
<body>
  <div id=""page"">
    <header><h1>ISBN Search</h1></header>
    <p>No results found for your query.</p>
  </div>
</body>
</html>";

    private static IsbnSearchOrgClient CreateClient(string responseHtml, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(responseHtml, statusCode);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new System.Uri("https://isbnsearch.org/")
        };
        return new IsbnSearchOrgClient(httpClient);
    }

    [Fact]
    public async Task FetchAsync_ValidIsbn_ReturnsBookMetadata()
    {
        var client = CreateClient(ValidIsbnSearchOrgHtml);
        var result = await client.FetchAsync("9780451524935", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("1984", result.Title);
        Assert.Contains("George Orwell", result.Authors);
        Assert.Equal("Penguin Publishing Group", result.Publisher);
        Assert.Equal("1950-07-01", result.PubDate);
        Assert.Equal("https://images.isbndb.com/covers/20614433482348.jpg", result.CoverImageUrl);
        Assert.Null(result.Language);
        Assert.Equal("9780451524935", result.Isbn);
        Assert.Equal("IsbnSearchOrg", result.SourceName);
    }

    [Fact]
    public async Task FetchAsync_BookNotFound_ReturnsNull()
    {
        var client = CreateClient(BookNotFoundHtml);
        var result = await client.FetchAsync("9780451524935", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchAsync_HttpRequest404_ReturnsNull()
    {
        var client = CreateClient("Not Found", HttpStatusCode.NotFound);
        var result = await client.FetchAsync("9780000000000", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchAsync_MultipleAuthors_SplitsCorrectly()
    {
        const string MultipleAuthorsHtml = @"
<div id=""book"">
  <div class=""bookinfo"">
    <h1>Test Book</h1>
    <p><strong>Author:</strong> Author One, Author Two, Author Three</p>
    <p><strong>Publisher:</strong> Test Publisher</p>
    <p><strong>Published:</strong> 2026-01-01</p>
  </div>
</div>";

        var client = CreateClient(MultipleAuthorsHtml);
        var result = await client.FetchAsync("9781234567890", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test Book", result.Title);
        Assert.Equal(3, result.Authors.Count);
        Assert.Equal("Author One", result.Authors[0]);
        Assert.Equal("Author Two", result.Authors[1]);
        Assert.Equal("Author Three", result.Authors[2]);
    }

    [Fact]
    public async Task FetchAsync_HtmlEntitiesDecoded()
    {
        const string EscapedHtml = @"
<div id=""book"">
  <div class=""bookinfo"">
    <h1>Title &amp; Subtitle &quot;Quotes&quot;</h1>
    <p><strong>Author:</strong> Author &apos;Name&apos;, Another &amp; Partner</p>
    <p><strong>Publisher:</strong> Publisher &amp; Co</p>
    <p><strong>Published:</strong> 2026</p>
  </div>
</div>";

        var client = CreateClient(EscapedHtml);
        var result = await client.FetchAsync("9781234567890", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Title & Subtitle \"Quotes\"", result.Title);
        Assert.Equal(2, result.Authors.Count);
        Assert.Equal("Author 'Name'", result.Authors[0]);
        Assert.Equal("Another & Partner", result.Authors[1]);
        Assert.Equal("Publisher & Co", result.Publisher);
    }
}
