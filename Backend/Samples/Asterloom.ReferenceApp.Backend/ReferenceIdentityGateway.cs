using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Rpc;

namespace Asterloom.ReferenceApp.Backend;

internal sealed class ReferenceIdentityGateway : IDisposable
{
    private readonly AsterloomAuthenticatedTransport _transport;

    public ReferenceIdentityGateway(
        AsterloomIdentityClient identity,
        ReferenceIdentityOptions options)
    {
        _transport = AsterloomAuthenticatedTransport.Create(
            options.AsterloomBaseAddress,
            cancellationToken => identity.GetServiceAccessTokenAsync(
                cancellationToken: cancellationToken),
            allowInsecureHttpForDevelopment: options.AllowInsecureHttpForDevelopment);
        Accounts = new AsterloomIdentityAccessClient(_transport.CallInvoker);
    }

    public AsterloomIdentityAccessClient Accounts { get; }

    public void Dispose() => _transport.Dispose();
}
