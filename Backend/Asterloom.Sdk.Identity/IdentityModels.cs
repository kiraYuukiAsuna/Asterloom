using System.Security.Claims;

namespace Asterloom.Sdk.Identity;

public sealed record AsterloomIdentityPage<T>(
    IReadOnlyList<T> Items,
    string? NextPageToken);

public enum AsterloomPassportRole
{
    SuperAdministrator = 0,
    TenantAdministrator = 1,
    Operator = 2,
    Developer = 3,
    Viewer = 4,
}

public enum AsterloomIdentityUserStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2,
    Archived = 3,
}

public sealed record AsterloomIdentityUser(
    Guid Id,
    string Email,
    string DisplayName,
    AsterloomIdentityUserStatus Status,
    long Version,
    IReadOnlyList<AsterloomPassportRole> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt,
    bool EmailConfirmed);

public sealed record AsterloomIdentityUserRegistration(
    string Email,
    string DisplayName,
    string Password,
    bool EmailConfirmed,
    IReadOnlyCollection<AsterloomPassportRole> Roles);

public enum AsterloomApplicationMembershipStatus
{
    Active = 0,
    Removed = 1,
}

public sealed record AsterloomApplicationMembership(
    Guid UserId,
    Guid TenantId,
    Guid ApplicationId,
    AsterloomApplicationMembershipStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AsterloomApplicationAccount(
    AsterloomIdentityUser User,
    AsterloomApplicationMembership Membership);

public sealed record AsterloomAccountRegistrationResult(
    AsterloomIdentityUser User,
    AsterloomApplicationMembership Membership,
    bool AccountCreated,
    bool VerificationRequired,
    string? EmailVerificationToken)
{
    public override string ToString() =>
        $"{nameof(AsterloomAccountRegistrationResult)} {{ User = {User}, "
        + $"Membership = {Membership}, AccountCreated = {AccountCreated}, "
        + $"VerificationRequired = {VerificationRequired}, "
        + "EmailVerificationToken = [REDACTED] }";
}

public sealed record AsterloomUserInvitation(
    AsterloomIdentityUser User,
    Uri InvitationUri,
    DateTimeOffset ExpiresAt);

public enum AsterloomIdentitySessionStatus
{
    Valid = 0,
    Revoked = 1,
}

public sealed record AsterloomIdentitySession(
    string Id,
    Guid UserId,
    string ClientId,
    string ClientDisplayName,
    IReadOnlyList<string> Scopes,
    AsterloomIdentitySessionStatus Status,
    DateTimeOffset CreatedAt);

public enum AsterloomOidcApplicationType
{
    Web = 0,
    Native = 1,
}

public enum AsterloomOidcClientType
{
    Public = 0,
    Confidential = 1,
}

public enum AsterloomOidcGrantType
{
    AuthorizationCode = 0,
    ClientCredentials = 1,
    RefreshToken = 2,
}

public sealed record AsterloomOidcClient(
    string Id,
    string ClientId,
    string DisplayName,
    AsterloomOidcApplicationType ApplicationType,
    AsterloomOidcClientType ClientType,
    IReadOnlyList<AsterloomOidcGrantType> GrantTypes,
    IReadOnlyList<Uri> RedirectUris,
    IReadOnlyList<Uri> PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    string Version,
    Guid? TenantId,
    Guid? ApplicationId,
    bool AllowUserRegistration,
    bool AllowMembershipAutoJoin,
    bool IsSystem = false,
    bool IsMutable = true);

public sealed record AsterloomOidcClientCredential(
    AsterloomOidcClient Client,
    string ClientSecret)
{
    public override string ToString() =>
        $"{nameof(AsterloomOidcClientCredential)} {{ Client = {Client}, ClientSecret = [REDACTED] }}";
}

public sealed record AsterloomOidcClientRegistration(
    string ClientId,
    string DisplayName,
    AsterloomOidcApplicationType ApplicationType,
    AsterloomOidcClientType ClientType,
    IReadOnlyCollection<AsterloomOidcGrantType> GrantTypes,
    IReadOnlyCollection<Uri> RedirectUris,
    IReadOnlyCollection<Uri> PostLogoutRedirectUris,
    IReadOnlyCollection<string> Scopes,
    Guid? TenantId = null,
    Guid? ApplicationId = null,
    bool AllowUserRegistration = false,
    bool AllowMembershipAutoJoin = false);

public sealed record AsterloomOidcClientUpdate(
    string DisplayName,
    IReadOnlyCollection<AsterloomOidcGrantType> GrantTypes,
    IReadOnlyCollection<Uri> RedirectUris,
    IReadOnlyCollection<Uri> PostLogoutRedirectUris,
    IReadOnlyCollection<string> Scopes,
    Guid? TenantId = null,
    Guid? ApplicationId = null,
    bool AllowUserRegistration = false,
    bool AllowMembershipAutoJoin = false);

public sealed record AsterloomOidcScope(
    string Id,
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Resources,
    string Version,
    bool IsSystem = false,
    bool IsMutable = true);

public sealed record AsterloomOidcScopeRegistration(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyCollection<string> Resources);

public sealed record AsterloomOidcScopeUpdate(
    string DisplayName,
    string Description,
    IReadOnlyCollection<string> Resources);

public sealed record AsterloomTokenSet(
    string AccessToken,
    DateTimeOffset? AccessTokenExpiresAt,
    string? IdentityToken,
    string? RefreshToken,
    ClaimsPrincipal Principal)
{
    public override string ToString() =>
        $"{nameof(AsterloomTokenSet)} {{ AccessToken = [REDACTED], "
        + $"AccessTokenExpiresAt = {AccessTokenExpiresAt:O}, "
        + "IdentityToken = [REDACTED], RefreshToken = [REDACTED], "
        + $"Principal = {Principal.Identity?.Name ?? "anonymous"} }}";
}
