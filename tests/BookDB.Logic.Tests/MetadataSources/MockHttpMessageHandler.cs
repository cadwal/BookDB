using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Tests.MetadataSources;

internal class MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
{
    private readonly string _responseContent = responseContent;
    private readonly HttpStatusCode _statusCode = statusCode;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
