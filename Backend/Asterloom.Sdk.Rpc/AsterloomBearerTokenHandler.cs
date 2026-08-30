using System.Net.Http.Headers;

namespace Asterloom.Sdk.Rpc;

/// <summary>
/// Adds the current Passport access token to HTTP and gRPC-over-HTTP requests.
/// </summary>
public sealed class AsterloomBearerTokenHandler : DelegatingHandler
{
    private readonly Uri _trustedBaseAddress;
    private readonly Func<CancellationToken, Task<string>> _accessTokenProvider;

    public AsterloomBearerTokenHandler(
        Uri trustedBaseAddress,
        Func<CancellationToken, Task<string>> accessTokenProvider,
        HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new SocketsHttpHandler())
    {
        ArgumentNullException.ThrowIfNull(trustedBaseAddress);
        if (!trustedBaseAddress.IsAbsoluteUri
            || (!string.Equals(
                    trustedBaseAddress.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    trustedBaseAddress.Scheme,
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The trusted Asterloom address must be an absolute HTTP(S) URI.",
                nameof(trustedBaseAddress));
        }

        _trustedBaseAddress = trustedBaseAddress;
        _accessTokenProvider = accessTokenProvider
            ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ShouldAttachBearerToken(request))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var token = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "The Passport token provider returned an empty access token.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private bool ShouldAttachBearerToken(HttpRequestMessage request)
    {
        var requestUri = request.RequestUri;
        if (request.Headers.Authorization is not null
            || requestUri is null
            || !requestUri.IsAbsoluteUri
            || Uri.Compare(
                requestUri,
                _trustedBaseAddress,
                UriComponents.SchemeAndServer,
                UriFormat.Unescaped,
                StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        // AWS Signature V4 transfer URLs authenticate through their query string.
        // Adding Passport's Authorization header makes S3-compatible servers reject
        // the request as having multiple authentication mechanisms.
        return requestUri.Query.IndexOf(
            "X-Amz-Signature=",
            StringComparison.OrdinalIgnoreCase) < 0;
    }
}
