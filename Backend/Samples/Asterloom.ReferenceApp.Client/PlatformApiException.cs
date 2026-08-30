using System.Net;

namespace Asterloom.ReferenceApp.Client;

internal sealed class PlatformApiException(
    HttpMethod method,
    Uri? requestUri,
    HttpStatusCode statusCode,
    string responseBody) : HttpRequestException(
        $"{method} {requestUri} returned HTTP {(int)statusCode}: {responseBody}",
        inner: null,
        statusCode)
{
    public string ResponseBody { get; } = responseBody;
}
