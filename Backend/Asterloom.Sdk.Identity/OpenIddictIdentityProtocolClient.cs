using System.Security.Claims;
using OpenIddict.Client;
using static OpenIddict.Client.OpenIddictClientModels;

namespace Asterloom.Sdk.Identity;

internal interface IAsterloomIdentityProtocolClient
{
    Task<AsterloomProtocolTokenResult> AuthenticateInteractivelyAsync(
        string registrationId,
        string? loginHint,
        CancellationToken cancellationToken);

    Task<AsterloomProtocolTokenResult> AuthenticateWithRefreshTokenAsync(
        string registrationId,
        string refreshToken,
        CancellationToken cancellationToken);

    Task<AsterloomProtocolTokenResult> AuthenticateWithClientCredentialsAsync(
        string registrationId,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken);

    Task<AsterloomProtocolTokenResult> AuthenticateWithPasswordAsync(
        string registrationId,
        string username,
        string password,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken);

    Task SignOutInteractivelyAsync(
        string registrationId,
        string? identityTokenHint,
        CancellationToken cancellationToken);
}

internal sealed record AsterloomProtocolTokenResult(
    string? AccessToken,
    DateTimeOffset? AccessTokenExpiresAt,
    string? IdentityToken,
    string? RefreshToken,
    ClaimsPrincipal? Principal);

internal sealed class OpenIddictIdentityProtocolClient(OpenIddictClientService client)
    : IAsterloomIdentityProtocolClient
{
    private readonly OpenIddictClientService _client =
        client ?? throw new ArgumentNullException(nameof(client));

    public async Task<AsterloomProtocolTokenResult> AuthenticateInteractivelyAsync(
        string registrationId,
        string? loginHint,
        CancellationToken cancellationToken)
    {
        var challenge = await _client.ChallengeInteractivelyAsync(
            new InteractiveChallengeRequest
            {
                RegistrationId = registrationId,
                LoginHint = loginHint,
                CancellationToken = cancellationToken,
            }).ConfigureAwait(false);
        var result = await _client.AuthenticateInteractivelyAsync(
            new InteractiveAuthenticationRequest
            {
                Nonce = challenge.Nonce,
                CancellationToken = cancellationToken,
            }).ConfigureAwait(false);

        return new(
            FirstNonEmpty(result.BackchannelAccessToken, result.FrontchannelAccessToken),
            result.BackchannelAccessTokenExpirationDate
                ?? result.FrontchannelAccessTokenExpirationDate,
            FirstNonEmpty(result.BackchannelIdentityToken, result.FrontchannelIdentityToken),
            result.RefreshToken,
            result.Principal
                ?? result.BackchannelIdentityTokenPrincipal
                ?? result.FrontchannelIdentityTokenPrincipal
                ?? result.UserInfoTokenPrincipal);
    }

    public async Task<AsterloomProtocolTokenResult> AuthenticateWithRefreshTokenAsync(
        string registrationId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var result = await _client.AuthenticateWithRefreshTokenAsync(
            new RefreshTokenAuthenticationRequest
            {
                RegistrationId = registrationId,
                RefreshToken = refreshToken,
                CancellationToken = cancellationToken,
            }).ConfigureAwait(false);
        return new(
            result.AccessToken,
            result.AccessTokenExpirationDate,
            result.IdentityToken,
            result.RefreshToken,
            result.Principal);
    }

    public async Task<AsterloomProtocolTokenResult> AuthenticateWithClientCredentialsAsync(
        string registrationId,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken)
    {
        var result = await _client.AuthenticateWithClientCredentialsAsync(
            new ClientCredentialsAuthenticationRequest
            {
                RegistrationId = registrationId,
                Scopes = [.. scopes],
                CancellationToken = cancellationToken,
            }).ConfigureAwait(false);
        return new(
            result.AccessToken,
            result.AccessTokenExpirationDate,
            result.IdentityToken,
            result.RefreshToken,
            result.Principal);
    }

    public async Task<AsterloomProtocolTokenResult> AuthenticateWithPasswordAsync(
        string registrationId,
        string username,
        string password,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken)
    {
        var result = await _client.AuthenticateWithPasswordAsync(
            new PasswordAuthenticationRequest
            {
                RegistrationId = registrationId,
                Username = username,
                Password = password,
                Scopes = [.. scopes],
                CancellationToken = cancellationToken,
            }).ConfigureAwait(false);
        return new(
            result.AccessToken,
            result.AccessTokenExpirationDate,
            result.IdentityToken,
            result.RefreshToken,
            result.Principal
                ?? result.IdentityTokenPrincipal
                ?? result.UserInfoTokenPrincipal);
    }

    public async Task SignOutInteractivelyAsync(
        string registrationId,
        string? identityTokenHint,
        CancellationToken cancellationToken)
    {
        var signOut = await _client.SignOutInteractivelyAsync(
            new InteractiveSignOutRequest
            {
                RegistrationId = registrationId,
                IdentityTokenHint = identityTokenHint,
                CancellationToken = cancellationToken,
            }).ConfigureAwait(false);
        await _client.AuthenticateInteractivelyAsync(
            new InteractiveAuthenticationRequest
            {
                Nonce = signOut.Nonce,
                CancellationToken = cancellationToken,
            }).ConfigureAwait(false);
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;
}
