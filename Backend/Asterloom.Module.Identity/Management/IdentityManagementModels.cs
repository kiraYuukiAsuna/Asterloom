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
    DateTimeOffset? ArchivedAt,
    bool EmailConfirmed);

public sealed record ManagedApplicationMembership(
    Guid UserId,
    Guid TenantId,
    Guid ApplicationId,
    Model.AsterloomApplicationMembershipStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ManagedApplicationAccount(
    ManagedIdentityUser User,
    ManagedApplicationMembership Membership);

public sealed record ManagedAccountRegistration(
    ManagedIdentityUser User,
    ManagedApplicationMembership Membership,
    bool AccountCreated,
    bool VerificationRequired,
    string EmailVerificationToken);

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
    string Version,
    Guid? TenantId,
    Guid? ApplicationId,
    bool AllowUserRegistration,
    bool AllowMembershipAutoJoin,
    bool IsSystem,
    bool IsMutable);

public sealed record ManagedOidcClientCredential(
    ManagedOidcClient Client,
    string ClientSecret);

public sealed record ManagedOidcScope(
    string Id,
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Resources,
    string Version,
    bool IsSystem,
    bool IsMutable);

public sealed record ManagedIdentitySession(
    string Id,
    Guid UserId,
    string ClientId,
    string ClientDisplayName,
    IReadOnlyList<string> Scopes,
    bool IsRevoked,
    DateTimeOffset CreatedAt);
