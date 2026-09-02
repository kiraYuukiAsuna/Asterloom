using System.Security.Claims;

namespace Asterloom.Sdk.Identity;

public sealed class AsterloomIdentityClient : IDisposable
{
    private readonly IAsterloomIdentityProtocolClient _protocol;
    private readonly AsterloomIdentityClientOptions _options;
    private readonly IAsterloomTokenStore _tokenStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _disposed;

    internal AsterloomIdentityClient(
        IAsterloomIdentityProtocolClient protocol,
        AsterloomIdentityClientOptions options,
        IAsterloomTokenStore tokenStore,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenStore);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _protocol = protocol;
        _options = options;
        _tokenStore = tokenStore;
        _timeProvider = timeProvider;
    }

    public async Task<AsterloomTokenSet> SignInAsync(
        string? loginHint = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInteractiveAuthentication();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _protocol.AuthenticateInteractivelyAsync(
                _options.RegistrationId,
                string.IsNullOrWhiteSpace(loginHint) ? null : loginHint.Trim(),
                cancellationToken).ConfigureAwait(false);
            var tokens = MapResult(result, current: null);
            await _tokenStore.WriteAsync(tokens, cancellationToken).ConfigureAwait(false);
            return tokens;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public ValueTask<AsterloomTokenSet?> GetStoredTokensAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _tokenStore.ReadAsync(cancellationToken);
    }

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var current = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (HasUsableAccessToken(current))
        {
            return current!.AccessToken;
        }

        if (_options.EnableServiceCredentials)
        {
            return (await GetServiceTokenSetAsync(
                forceRefresh: false,
                cancellationToken).ConfigureAwait(false)).AccessToken;
        }

        if (string.IsNullOrWhiteSpace(current?.RefreshToken))
        {
            throw new InvalidOperationException(
                "No usable access token or refresh token is available. Sign in interactively first.");
        }

        return (await RefreshTokenSetAsync(
            forceRefresh: false,
            cancellationToken).ConfigureAwait(false)).AccessToken;
    }

    public Task<AsterloomTokenSet> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInteractiveAuthentication();
        return RefreshTokenSetAsync(forceRefresh: true, cancellationToken);
    }

    private async Task<AsterloomTokenSet> RefreshTokenSetAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!forceRefresh && HasUsableAccessToken(current))
            {
                return current!;
            }

            if (string.IsNullOrWhiteSpace(current?.RefreshToken))
            {
                throw new InvalidOperationException(
                    "No refresh token is available. Sign in interactively first.");
            }

            var result = await _protocol.AuthenticateWithRefreshTokenAsync(
                _options.RegistrationId,
                current.RefreshToken,
                cancellationToken).ConfigureAwait(false);
            var tokens = MapResult(result, current);
            await _tokenStore.WriteAsync(tokens, cancellationToken).ConfigureAwait(false);
            return tokens;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<string> GetServiceAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureServiceCredentials();
        return (await GetServiceTokenSetAsync(
            forceRefresh,
            cancellationToken).ConfigureAwait(false)).AccessToken;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInteractiveAuthentication();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return;
            }

            try
            {
                await _protocol.SignOutInteractivelyAsync(
                    _options.RegistrationId,
                    current.IdentityToken,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await _tokenStore.ClearAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public ValueTask ClearLocalSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _tokenStore.ClearAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operationLock.Dispose();
    }

    private async Task<AsterloomTokenSet> GetServiceTokenSetAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        EnsureServiceCredentials();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!forceRefresh && HasUsableAccessToken(current))
            {
                return current!;
            }

            var result = await _protocol.AuthenticateWithClientCredentialsAsync(
                _options.RegistrationId,
                _options.Scopes.ToArray(),
                cancellationToken).ConfigureAwait(false);
            var tokens = MapResult(result, current: null);
            await _tokenStore.WriteAsync(tokens, cancellationToken).ConfigureAwait(false);
            return tokens;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private static AsterloomTokenSet MapResult(
        AsterloomProtocolTokenResult result,
        AsterloomTokenSet? current) => new(
            RequireAccessToken(result.AccessToken),
            result.AccessTokenExpiresAt,
            FirstNonEmpty(result.IdentityToken, current?.IdentityToken),
            FirstNonEmpty(result.RefreshToken, current?.RefreshToken),
            result.Principal
                ?? current?.Principal
                ?? new ClaimsPrincipal(new ClaimsIdentity()));

    private bool HasUsableAccessToken(AsterloomTokenSet? tokens)
    {
        if (string.IsNullOrWhiteSpace(tokens?.AccessToken))
        {
            return false;
        }

        return tokens.AccessTokenExpiresAt is null
            || tokens.AccessTokenExpiresAt.Value
                > _timeProvider.GetUtcNow() + _options.RefreshBeforeExpiration;
    }

    private void EnsureInteractiveAuthentication()
    {
        if (!_options.EnableInteractiveAuthentication)
        {
            throw new InvalidOperationException(
                "Interactive authentication is not enabled for this client registration.");
        }
    }

    private void EnsureServiceCredentials()
    {
        if (!_options.EnableServiceCredentials)
        {
            throw new InvalidOperationException(
                "Service credential authentication is not enabled for this client registration.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static string RequireAccessToken(string? value) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                "Passport completed the token operation without returning an access token.");

    private static string? FirstNonEmpty(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;
}
