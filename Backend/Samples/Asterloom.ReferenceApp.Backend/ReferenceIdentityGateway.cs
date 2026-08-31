using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Rpc;

namespace Asterloom.ReferenceApp.Backend;

internal sealed class ReferenceIdentityGateway : IDisposable
{
    private readonly AsterloomIdentityClient _identity;
    private readonly AsterloomAuthenticatedTransport _transport;

    public ReferenceIdentityGateway(
        AsterloomIdentityClient identity,
        ReferenceIdentityOptions options)
    {
        _identity = identity;
        _transport = AsterloomAuthenticatedTransport.Create(
            options.AsterloomBaseAddress,
            cancellationToken => identity.GetServiceAccessTokenAsync(
                cancellationToken: cancellationToken),
            allowInsecureHttpForDevelopment: options.AllowInsecureHttpForDevelopment);
        Accounts = new AsterloomIdentityAccessClient(_transport.CallInvoker);
    }

    public AsterloomIdentityAccessClient Accounts { get; }

    public Task<AsterloomTokenSet> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken) =>
        _identity.AuthenticateWithPasswordAsync(email, password, cancellationToken);

    public Task<AsterloomTokenSet> RefreshAsync(
        AsterloomTokenSet tokens,
        CancellationToken cancellationToken) =>
        _identity.RefreshUserTokensAsync(tokens, cancellationToken);

    public void Dispose() => _transport.Dispose();
}
