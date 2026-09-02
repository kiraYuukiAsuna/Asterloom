using System.Net.Http.Headers;
using Asterloom.Sdk.Identity;

namespace Asterloom.ReferenceApp.Backend;

internal sealed class ReferenceServiceTokenHandler(AsterloomIdentityClient identity)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await identity.GetServiceAccessTokenAsync(
            cancellationToken: cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
