namespace Asterloom.Modules.Identity.Management;

public sealed record IdentityPage<T>(
    IReadOnlyList<T> Items,
    string NextPageToken);

public sealed record ManagedIdentityUser(
    Guid Id,
    string Email,
    string DisplayName,
    Model.AsterloomUserStatus Status,
    long Version,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record ManagedUserInvitation(
    ManagedIdentityUser User,
    string InvitationUrl,
    DateTimeOffset ExpiresAt);

public enum ManagedOidcClientType
{
    Public = 0,
    Confidential = 1,
}

public enum ManagedOidcApplicationType
{
    Web = 0,
    Native = 1,
}

public enum ManagedOidcGrantType
{
    AuthorizationCode = 0,
    ClientCredentials = 1,
    RefreshToken = 2,
}

public sealed record ManagedOidcClient(
    string Id,
    string ClientId,
    string DisplayName,
    ManagedOidcClientType ClientType,
    ManagedOidcApplicationType ApplicationType,
    IReadOnlyList<ManagedOidcGrantType> GrantTypes,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    string Version);

public sealed record ManagedOidcClientCredential(
    ManagedOidcClient Client,
    string ClientSecret);

public sealed record ManagedOidcScope(
    string Id,
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Resources,
    string Version);

public sealed record ManagedIdentitySession(
    string Id,
    Guid UserId,
    string ClientId,
    string ClientDisplayName,
    IReadOnlyList<string> Scopes,
    bool IsRevoked,
    DateTimeOffset CreatedAt);
