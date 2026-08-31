using System.Collections.Concurrent;
using System.Security.Cryptography;
using Asterloom.Sdk.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.ReferenceApp.Backend;

internal sealed class ReferenceIdentitySessionStore(
    ReferenceIdentityGateway gateway,
    TimeProvider timeProvider)
{
    public const string CookieName = "asterloom-reference-session";

    private readonly ConcurrentDictionary<string, AsterloomTokenSet> _sessions =
        new(StringComparer.Ordinal);

    public string Create(AsterloomTokenSet tokens)
    {
        var sessionId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _sessions[sessionId] = tokens;
        return sessionId;
    }

    public async Task<AsterloomTokenSet?> GetAsync(
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || !_sessions.TryGetValue(sessionId, out var tokens))
        {
            return null;
        }

        var refreshAt = timeProvider.GetUtcNow().AddMinutes(1);
        if (tokens.AccessTokenExpiresAt is null || tokens.AccessTokenExpiresAt > refreshAt)
        {
            return tokens;
        }

        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            _sessions.TryRemove(sessionId, out _);
            return null;
        }

        try
        {
            var refreshed = await gateway.RefreshAsync(tokens, cancellationToken);
            _sessions[sessionId] = refreshed;
            return refreshed;
        }
        catch
        {
            _sessions.TryRemove(sessionId, out _);
            throw;
        }
    }

    public void Remove(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _sessions.TryRemove(sessionId, out _);
        }
    }
}
