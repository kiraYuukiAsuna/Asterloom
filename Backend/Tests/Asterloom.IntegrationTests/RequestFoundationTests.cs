using Microsoft.AspNetCore.Mvc.Testing;

namespace Asterloom.IntegrationTests;

public sealed class RequestFoundationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RequestFoundationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ValidRequestIdIsEchoed()
    {
        const string requestId = "request-foundation-0001";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Request-ID", requestId);

        using var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(requestId, response.Headers.GetValues("X-Request-ID").Single());
    }

    [Fact]
    public async Task UnsafeRequestIdIsReplaced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Request-ID", "unsafe request id");

        using var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var responseRequestId = response.Headers.GetValues("X-Request-ID").Single();
        Assert.NotEqual("unsafe request id", responseRequestId);
        Assert.Matches("^[a-f0-9]{32}$", responseRequestId);
    }
}
