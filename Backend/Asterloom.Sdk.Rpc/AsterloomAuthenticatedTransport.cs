using Grpc.Core;
using Grpc.Net.Client;

namespace Asterloom.Sdk.Rpc;

/// <summary>
/// Owns one authenticated HTTP client and one gRPC channel for the same Asterloom endpoint.
/// </summary>
public sealed class AsterloomAuthenticatedTransport : IDisposable
{
    private bool _disposed;

    private AsterloomAuthenticatedTransport(HttpClient httpClient, GrpcChannel grpcChannel)
    {
        HttpClient = httpClient;
        GrpcChannel = grpcChannel;
    }

    public HttpClient HttpClient { get; }

    public GrpcChannel GrpcChannel { get; }

    public CallInvoker CallInvoker => GrpcChannel.CreateCallInvoker();

    public static AsterloomAuthenticatedTransport Create(
        Uri baseAddress,
        Func<CancellationToken, Task<string>> accessTokenProvider,
        HttpMessageHandler? innerHandler = null,
        bool allowInsecureHttpForDevelopment = false)
    {
        ValidateBaseAddress(baseAddress, allowInsecureHttpForDevelopment);
        ArgumentNullException.ThrowIfNull(accessTokenProvider);

        var handler = new AsterloomBearerTokenHandler(
            baseAddress,
            accessTokenProvider,
            innerHandler);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = EnsureTrailingSlash(baseAddress),
        };

        try
        {
            var channel = GrpcChannel.ForAddress(
                httpClient.BaseAddress,
                new GrpcChannelOptions
                {
                    HttpClient = httpClient,
                    DisposeHttpClient = false,
                });
            return new AsterloomAuthenticatedTransport(httpClient, channel);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GrpcChannel.Dispose();
        HttpClient.Dispose();
    }

    private static void ValidateBaseAddress(Uri baseAddress, bool allowInsecureHttp)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (!baseAddress.IsAbsoluteUri
            || !string.IsNullOrEmpty(baseAddress.UserInfo)
            || !string.IsNullOrEmpty(baseAddress.Query)
            || !string.IsNullOrEmpty(baseAddress.Fragment))
        {
            throw new ArgumentException(
                "The Asterloom base address must be an absolute HTTP(S) URI without credentials, a query, or a fragment.",
                nameof(baseAddress));
        }

        if (string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!allowInsecureHttp
            || !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || !baseAddress.IsLoopback)
        {
            throw new ArgumentException(
                "Asterloom endpoints must use HTTPS. Plain HTTP is limited to explicitly enabled loopback development endpoints.",
                nameof(baseAddress));
        }
    }

    private static Uri EnsureTrailingSlash(Uri value)
    {
        if (value.AbsolutePath.EndsWith('/'))
        {
            return value;
        }

        var builder = new UriBuilder(value)
        {
            Path = value.AbsolutePath + "/",
        };
        return builder.Uri;
    }
}
