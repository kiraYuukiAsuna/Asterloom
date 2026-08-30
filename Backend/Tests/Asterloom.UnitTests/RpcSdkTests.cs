using System.Net;
using System.Net.Http.Headers;
using Asterloom.Sdk.Rpc;

namespace Asterloom.UnitTests;

public sealed class RpcSdkTests
{
    [Fact]
    public async Task AuthenticatedTransportSharesBearerAuthenticationWithHttpAndGrpc()
    {
        var capture = new CapturingHandler();
        using var transport = AsterloomAuthenticatedTransport.Create(
            new Uri("http://localhost:5080/platform"),
            _ => Task.FromResult("reference-access-token"),
            capture,
            allowInsecureHttpForDevelopment: true);

        using var response = await transport.HttpClient.GetAsync("api/v1/operations/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new AuthenticationHeaderValue("Bearer", "reference-access-token"),
            capture.Authorization);
        Assert.Equal(
            new Uri("http://localhost:5080/platform/api/v1/operations/health"),
            capture.RequestUri);
        Assert.NotNull(transport.CallInvoker);
    }

    [Theory]
    [InlineData("http://asterloom.example/")]
    [InlineData("ftp://localhost/")]
    [InlineData("https://user:password@asterloom.example/")]
    [InlineData("https://asterloom.example/?query=invalid")]
    public void AuthenticatedTransportRejectsUnsafeBaseAddresses(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            AsterloomAuthenticatedTransport.Create(
                new Uri(value),
                _ => Task.FromResult("token")));

        Assert.Equal("baseAddress", exception.ParamName);
    }

    [Fact]
    public async Task BearerHandlerRejectsEmptyTokensBeforeSending()
    {
        using var client = new HttpClient(new AsterloomBearerTokenHandler(
            new Uri("https://asterloom.example/"),
            _ => Task.FromResult(string.Empty),
            new CapturingHandler()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetAsync("https://asterloom.example/api/v1/operations/health"));

        Assert.Contains("empty access token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BearerHandlerDoesNotLeakTokenToAnotherOrigin()
    {
        var capture = new CapturingHandler();
        var tokenRequests = 0;
        using var client = new HttpClient(new AsterloomBearerTokenHandler(
            new Uri("https://asterloom.example/"),
            _ =>
            {
                tokenRequests++;
                return Task.FromResult("reference-access-token");
            },
            capture));

        using var response = await client.GetAsync("https://objects.example/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(capture.Authorization);
        Assert.Equal(0, tokenRequests);
    }

    [Fact]
    public async Task BearerHandlerLeavesAwsPresignedRequestsUnauthenticated()
    {
        var capture = new CapturingHandler();
        var tokenRequests = 0;
        using var client = new HttpClient(new AsterloomBearerTokenHandler(
            new Uri("https://asterloom.example/"),
            _ =>
            {
                tokenRequests++;
                return Task.FromResult("reference-access-token");
            },
            capture));

        using var response = await client.PutAsync(
            "https://asterloom.example/asterloom-objects/item"
                + "?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Signature=abc123",
            new ByteArrayContent([1, 2, 3]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(capture.Authorization);
        Assert.Equal(0, tokenRequests);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
