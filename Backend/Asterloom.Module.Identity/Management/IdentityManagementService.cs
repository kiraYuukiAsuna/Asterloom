using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Identity.Bootstrap;
using Asterloom.Modules.Identity.Model;
using Asterloom.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.Modules.Identity.Management;

public sealed partial class IdentityManagementService(
    AsterloomIdentityDbContext database,
    UserManager<AsterloomUser> userManager,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    IOpenIddictScopeManager scopeManager,
    IdentitySecurityOptions securityOptions,
    IdentityBootstrapOptions bootstrapOptions,
    IdentityPersistenceOptions persistenceOptions,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(24);

    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const string ApiScope = "asterloom.api";

    private static readonly HashSet<string> StandardScopes = new(
        [Scopes.OpenId, Scopes.OfflineAccess, Scopes.Email, Scopes.Profile, Scopes.Roles],
        StringComparer.Ordinal);

    public async Task<IdentityPage<ManagedIdentityUser>> ListUsersAsync(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var page = ParsePage(pageSize, pageToken);
        var normalizedQuery = NormalizeOptional(query);
        var usersQuery = database.Users.AsNoTracking();
        if (!includeArchived)
        {
            usersQuery = usersQuery.Where(user =>
                user.Status != AsterloomUserStatus.Archived);
        }

        if (normalizedQuery is not null)
        {
            usersQuery = usersQuery.Where(user =>
                user.DisplayName.Contains(normalizedQuery)
                || (user.Email != null && user.Email.Contains(normalizedQuery)));
        }

        var users = await usersQuery
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Skip(page.Offset)
            .Take(page.Size + 1)
            .ToListAsync(cancellationToken);
        var hasMore = users.Count > page.Size;
        if (hasMore)
        {
            users.RemoveAt(users.Count - 1);
        }

        var items = new List<ManagedIdentityUser>(users.Count);
        foreach (var user in users)
        {
            items.Add(await ToManagedUserAsync(user));
        }

        return new IdentityPage<ManagedIdentityUser>(
            items,
            hasMore ? EncodePageToken(page.Offset + items.Count) : string.Empty);
    }

    public async Task<ManagedIdentityUser> GetUserAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await ToManagedUserAsync(await RequireUserAsync(userId, cancellationToken));

    public async Task<ManagedUserInvitation> InviteUserAsync(
        string email,
        string displayName,
        IEnumerable<string> roles,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var normalizedRoles = NormalizeRoles(roles);
        if (await userManager.FindByEmailAsync(normalizedEmail) is not null)
        {
            throw AlreadyExists("identity_user_exists", "A user with this email already exists.");
        }

        var now = timeProvider.GetUtcNow();
        var user = new AsterloomUser
        {
            Id = Guid.CreateVersion7(),
            UserName = normalizedEmail,
            Email = normalizedEmail,
            EmailConfirmed = false,
            DisplayName = NormalizeDisplayName(displayName),
            Status = AsterloomUserStatus.Pending,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        EnsureSucceeded(
            await userManager.CreateAsync(user),
            "identity_user_create_failed",
            "Unable to create the invited user.");
        try
        {
            EnsureSucceeded(
                await userManager.AddToRolesAsync(user, normalizedRoles),
                "identity_user_roles_failed",
                "Unable to assign the invited user's roles.");
        }
        catch
        {
            await userManager.DeleteAsync(user);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await CreateInvitationAsync(user);
    }

    public async Task<ManagedUserInvitation> ResendInvitationAsync(
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        RequireVersion(user.Version, expectedVersion);
        if (user.Status != AsterloomUserStatus.Pending || user.EmailConfirmed)
        {
            throw FailedPrecondition(
                "identity_invitation_not_pending",
                "Only a pending user can receive another invitation.");
        }

        user.Version++;
        user.UpdatedAt = timeProvider.GetUtcNow();
        EnsureSucceeded(
            await userManager.UpdateSecurityStampAsync(user),
            "identity_invitation_reset_failed",
            "Unable to invalidate the previous invitation.");
        EnsureSucceeded(
            await userManager.UpdateAsync(user),
            "identity_user_conflict",
            "The user was changed by another request.",
            conflict: true);
        return await CreateInvitationAsync(user);
    }

    public async Task<ManagedIdentityUser> UpdateUserAsync(
        string userId,
        string displayName,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        RequireVersion(user.Version, expectedVersion);
        RequireNotArchived(user);
        user.DisplayName = NormalizeDisplayName(displayName);
        Touch(user);
        await UpdateUserRecordAsync(user);
        return await ToManagedUserAsync(user);
    }

    public async Task<ManagedIdentityUser> SetUserRolesAsync(
        string userId,
        IEnumerable<string> roles,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        RequireVersion(user.Version, expectedVersion);
        RequireNotArchived(user);
        var normalizedRoles = NormalizeRoles(roles);
        var currentRoles = await userManager.GetRolesAsync(user);
        if (user.Status == AsterloomUserStatus.Active
            && currentRoles.Contains(IdentityRoleCatalog.SuperAdministrator, StringComparer.Ordinal)
            && !normalizedRoles.Contains(
                IdentityRoleCatalog.SuperAdministrator,
                StringComparer.Ordinal))
        {
            await EnsureNotLastSuperAdministratorAsync(user);
        }

        var remove = currentRoles.Except(normalizedRoles, StringComparer.Ordinal).ToArray();
        var add = normalizedRoles.Except(currentRoles, StringComparer.Ordinal).ToArray();
        if (remove.Length > 0)
        {
            EnsureSucceeded(
                await userManager.RemoveFromRolesAsync(user, remove),
                "identity_user_roles_failed",
                "Unable to remove the user's previous roles.");
        }

        if (add.Length > 0)
        {
            EnsureSucceeded(
                await userManager.AddToRolesAsync(user, add),
                "identity_user_roles_failed",
                "Unable to assign the user's roles.");
        }

        Touch(user);
        await UpdateUserRecordAsync(user);
        return await ToManagedUserAsync(user);
    }

    public async Task<ManagedIdentityUser> SuspendUserAsync(
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        RequireVersion(user.Version, expectedVersion);
        RequireNotArchived(user);
        if (user.Status == AsterloomUserStatus.Suspended)
        {
            return await ToManagedUserAsync(user);
        }

        if (user.Status != AsterloomUserStatus.Active)
        {
            throw FailedPrecondition(
                "identity_user_not_active",
                "Only an active user can be suspended.");
        }

        if (await userManager.IsInRoleAsync(user, IdentityRoleCatalog.SuperAdministrator))
        {
            await EnsureNotLastSuperAdministratorAsync(user);
        }

        user.Status = AsterloomUserStatus.Suspended;
        Touch(user);
        await UpdateUserRecordAsync(user, rotateSecurityStamp: true);
        await RevokeUserSessionsCoreAsync(user.Id, cancellationToken);
        return await ToManagedUserAsync(user);
    }

    public async Task<ManagedIdentityUser> ReactivateUserAsync(
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        RequireVersion(user.Version, expectedVersion);
        if (user.Status != AsterloomUserStatus.Suspended)
        {
            throw FailedPrecondition(
                "identity_user_not_suspended",
                "Only a suspended user can be reactivated.");
        }

        if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw FailedPrecondition(
                "identity_user_invitation_incomplete",
                "The user must accept the invitation before activation.");
        }

        user.Status = AsterloomUserStatus.Active;
        Touch(user);
        await UpdateUserRecordAsync(user);
        return await ToManagedUserAsync(user);
    }

    public async Task<ManagedIdentityUser> ArchiveUserAsync(
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        RequireVersion(user.Version, expectedVersion);
        if (user.Status == AsterloomUserStatus.Archived)
        {
            return await ToManagedUserAsync(user);
        }

        if (await userManager.IsInRoleAsync(user, IdentityRoleCatalog.SuperAdministrator))
        {
            await EnsureNotLastSuperAdministratorAsync(user);
        }

        var now = timeProvider.GetUtcNow();
        user.Status = AsterloomUserStatus.Archived;
        user.ArchivedAt = now;
        user.UpdatedAt = now;
        user.Version++;
        await UpdateUserRecordAsync(user, rotateSecurityStamp: true);
        await RevokeUserSessionsCoreAsync(user.Id, cancellationToken);
        return await ToManagedUserAsync(user);
    }

    public async Task<ManagedIdentityUser> RestoreUserAsync(
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        RequireVersion(user.Version, expectedVersion);
        if (user.Status != AsterloomUserStatus.Archived)
        {
            throw FailedPrecondition(
                "identity_user_not_archived",
                "Only an archived user can be restored.");
        }

        user.Status = user.EmailConfirmed && !string.IsNullOrWhiteSpace(user.PasswordHash)
            ? AsterloomUserStatus.Active
            : AsterloomUserStatus.Pending;
        user.ArchivedAt = null;
        Touch(user);
        await UpdateUserRecordAsync(user);
        return await ToManagedUserAsync(user);
    }

    public async Task<IdentityPage<ManagedIdentitySession>> ListUserSessionsAsync(
        string userId,
        int pageSize,
        string? pageToken,
        bool includeRevoked,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        var page = ParsePage(pageSize, pageToken);
        var sessions = new List<ManagedIdentitySession>();
        await foreach (var authorization in authorizationManager
            .FindBySubjectAsync(user.Id.ToString("D", CultureInfo.InvariantCulture), cancellationToken))
        {
            var status = await authorizationManager.GetStatusAsync(
                authorization,
                cancellationToken);
            var revoked = !string.Equals(status, Statuses.Valid, StringComparison.Ordinal);
            if (revoked && !includeRevoked)
            {
                continue;
            }

            sessions.Add(await ToManagedSessionAsync(authorization, user.Id, revoked, cancellationToken));
        }

        var ordered = sessions
            .OrderByDescending(static session => session.CreatedAt)
            .ThenBy(static session => session.Id, StringComparer.Ordinal)
            .Skip(page.Offset)
            .Take(page.Size + 1)
            .ToList();
        var hasMore = ordered.Count > page.Size;
        if (hasMore)
        {
            ordered.RemoveAt(ordered.Count - 1);
        }

        return new IdentityPage<ManagedIdentitySession>(
            ordered,
            hasMore ? EncodePageToken(page.Offset + ordered.Count) : string.Empty);
    }

    public async Task<ManagedIdentitySession> RevokeUserSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        var authorization = await authorizationManager.FindByIdAsync(
                RequireText(sessionId, "sessionId", 200),
                cancellationToken)
            ?? throw NotFound("identity_session_not_found", "The session was not found.");
        var subject = await authorizationManager.GetSubjectAsync(authorization, cancellationToken);
        if (!string.Equals(subject, user.Id.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw NotFound("identity_session_not_found", "The session was not found.");
        }

        await authorizationManager.TryRevokeAsync(authorization, cancellationToken);
        var sessionTokens = await CollectAsync(
            tokenManager.FindByAuthorizationIdAsync(sessionId, cancellationToken),
            cancellationToken);
        foreach (var token in sessionTokens)
        {
            await tokenManager.TryRevokeAsync(token, cancellationToken);
        }
        return await ToManagedSessionAsync(authorization, user.Id, revoked: true, cancellationToken);
    }

    public async Task<long> RevokeAllUserSessionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        return await RevokeUserSessionsCoreAsync(user.Id, cancellationToken);
    }

    public async Task<IdentityPage<ManagedOidcClient>> ListClientsAsync(
        int pageSize,
        string? pageToken,
        string? query,
        CancellationToken cancellationToken)
    {
        var page = ParsePage(pageSize, pageToken);
        var normalizedQuery = NormalizeOptional(query);
        var items = new List<ManagedOidcClient>();
        await foreach (var application in applicationManager.ListAsync(cancellationToken: cancellationToken))
        {
            var item = await ToManagedClientAsync(application, cancellationToken);
            if (normalizedQuery is null
                || item.ClientId.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(item);
            }
        }

        var ordered = items
            .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ClientId, StringComparer.Ordinal)
            .Skip(page.Offset)
            .Take(page.Size + 1)
            .ToList();
        var hasMore = ordered.Count > page.Size;
        if (hasMore)
        {
            ordered.RemoveAt(ordered.Count - 1);
        }

        return new IdentityPage<ManagedOidcClient>(
            ordered,
            hasMore ? EncodePageToken(page.Offset + ordered.Count) : string.Empty);
    }

    public async Task<ManagedOidcClient> GetClientAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        var application = await RequireClientAsync(clientId, cancellationToken);
        return await ToManagedClientAsync(application, cancellationToken);
    }

    public async Task<ManagedOidcClientCredential> CreateClientAsync(
        string clientId,
        string displayName,
        ManagedOidcClientType clientType,
        ManagedOidcApplicationType applicationType,
        IEnumerable<ManagedOidcGrantType> grantTypes,
        IEnumerable<string> redirectUris,
        IEnumerable<string> postLogoutRedirectUris,
        IEnumerable<string> scopes,
        CancellationToken cancellationToken)
    {
        var normalizedClientId = NormalizeClientId(clientId);
        if (await applicationManager.FindByClientIdAsync(normalizedClientId, cancellationToken) is not null)
        {
            throw AlreadyExists("identity_client_exists", "An OIDC client with this client ID already exists.");
        }

        var normalizedGrants = NormalizeGrantTypes(grantTypes);
        var normalizedRedirects = NormalizeUris(
            redirectUris,
            "redirectUris",
            applicationType);
        var normalizedPostLogoutRedirects = NormalizeUris(
            postLogoutRedirectUris,
            "postLogoutRedirectUris",
            applicationType);
        var normalizedScopes = await NormalizeScopesAsync(scopes, cancellationToken);
        ValidateClient(clientType, applicationType, normalizedGrants, normalizedRedirects);
        var secret = clientType == ManagedOidcClientType.Confidential
            ? GenerateSecret()
            : string.Empty;
        var descriptor = CreateClientDescriptor(
            normalizedClientId,
            NormalizeDisplayName(displayName),
            clientType,
            applicationType,
            normalizedGrants,
            normalizedRedirects,
            normalizedPostLogoutRedirects,
            normalizedScopes,
            secret);
        var application = await applicationManager.CreateAsync(descriptor, cancellationToken);
        return new ManagedOidcClientCredential(
            await ToManagedClientAsync(application, cancellationToken),
            secret);
    }

    public async Task<ManagedOidcClient> UpdateClientAsync(
        string clientId,
        string displayName,
        IEnumerable<ManagedOidcGrantType> grantTypes,
        IEnumerable<string> redirectUris,
        IEnumerable<string> postLogoutRedirectUris,
        IEnumerable<string> scopes,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        RequireMutableClient(clientId);
        var application = await RequireClientAsync(clientId, cancellationToken);
        RequireVersion(GetApplicationVersion(application), expectedVersion);
        var current = await ToManagedClientAsync(application, cancellationToken);
        var normalizedGrants = NormalizeGrantTypes(grantTypes);
        var normalizedRedirects = NormalizeUris(
            redirectUris,
            "redirectUris",
            current.ApplicationType);
        var normalizedPostLogoutRedirects = NormalizeUris(
            postLogoutRedirectUris,
            "postLogoutRedirectUris",
            current.ApplicationType);
        var normalizedScopes = await NormalizeScopesAsync(scopes, cancellationToken);
        ValidateClient(
            current.ClientType,
            current.ApplicationType,
            normalizedGrants,
            normalizedRedirects);

        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
        descriptor.ApplicationType = current.ApplicationType == ManagedOidcApplicationType.Native
            ? ApplicationTypes.Native
            : ApplicationTypes.Web;
        descriptor.DisplayName = NormalizeDisplayName(displayName);
        ApplyClientConfiguration(
            descriptor,
            normalizedGrants,
            normalizedRedirects,
            normalizedPostLogoutRedirects,
            normalizedScopes);
        await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
        return await ToManagedClientAsync(application, cancellationToken);
    }

    public async Task<ManagedOidcClientCredential> RotateClientSecretAsync(
        string clientId,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        RequireMutableClient(clientId);
        var application = await RequireClientAsync(clientId, cancellationToken);
        RequireVersion(GetApplicationVersion(application), expectedVersion);
        if (!await applicationManager.HasClientTypeAsync(
            application,
            ClientTypes.Confidential,
            cancellationToken))
        {
            throw FailedPrecondition(
                "identity_client_is_public",
                "Public clients do not have a client secret.");
        }

        var secret = GenerateSecret();
        await applicationManager.UpdateAsync(application, secret, cancellationToken);
        return new ManagedOidcClientCredential(
            await ToManagedClientAsync(application, cancellationToken),
            secret);
    }

    public async Task<ManagedOidcClient> DeleteClientAsync(
        string clientId,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        var normalizedClientId = NormalizeClientId(clientId);
        RequireMutableClient(normalizedClientId);

        var application = await RequireClientAsync(normalizedClientId, cancellationToken);
        RequireVersion(GetApplicationVersion(application), expectedVersion);
        var item = await ToManagedClientAsync(application, cancellationToken);
        var clientAuthorizations = await CollectAsync(
            authorizationManager.FindByApplicationIdAsync(item.Id, cancellationToken),
            cancellationToken);
        foreach (var authorization in clientAuthorizations)
        {
            await authorizationManager.TryRevokeAsync(authorization, cancellationToken);
        }

        var clientTokens = await CollectAsync(
            tokenManager.FindByApplicationIdAsync(item.Id, cancellationToken),
            cancellationToken);
        foreach (var token in clientTokens)
        {
            await tokenManager.TryRevokeAsync(token, cancellationToken);
        }
        if (persistenceOptions.Provider == IdentityPersistenceProvider.Memory)
        {
            var tokens = await database
                .Set<OpenIddictEntityFrameworkCoreToken>()
                .Where(token => EF.Property<string?>(token, "ApplicationId") == item.Id)
                .ToListAsync(cancellationToken);
            var authorizations = await database
                .Set<OpenIddictEntityFrameworkCoreAuthorization>()
                .Where(authorization =>
                    EF.Property<string?>(authorization, "ApplicationId") == item.Id)
                .ToListAsync(cancellationToken);
            database.RemoveRange(tokens);
            database.RemoveRange(authorizations);
            database.Remove(application);
            await database.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await applicationManager.DeleteAsync(application, cancellationToken);
        }
        return item;
    }

    public async Task<IdentityPage<ManagedOidcScope>> ListScopesAsync(
        int pageSize,
        string? pageToken,
        string? query,
        CancellationToken cancellationToken)
    {
        var page = ParsePage(pageSize, pageToken);
        var normalizedQuery = NormalizeOptional(query);
        var items = new List<ManagedOidcScope>();
        await foreach (var scope in scopeManager.ListAsync(cancellationToken: cancellationToken))
        {
            var item = await ToManagedScopeAsync(scope, cancellationToken);
            if (normalizedQuery is null
                || item.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(item);
            }
        }

        var ordered = items
            .OrderBy(static item => item.Name, StringComparer.Ordinal)
            .Skip(page.Offset)
            .Take(page.Size + 1)
            .ToList();
        var hasMore = ordered.Count > page.Size;
        if (hasMore)
        {
            ordered.RemoveAt(ordered.Count - 1);
        }

        return new IdentityPage<ManagedOidcScope>(
            ordered,
            hasMore ? EncodePageToken(page.Offset + ordered.Count) : string.Empty);
    }

    public async Task<ManagedOidcScope> GetScopeAsync(
        string scopeId,
        CancellationToken cancellationToken) =>
        await ToManagedScopeAsync(
            await RequireScopeByIdAsync(scopeId, cancellationToken),
            cancellationToken);

    public async Task<ManagedOidcScope> CreateScopeAsync(
        string name,
        string displayName,
        string description,
        IEnumerable<string> resources,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeScopeName(name);
        if (StandardScopes.Contains(normalizedName)
            || await scopeManager.FindByNameAsync(normalizedName, cancellationToken) is not null)
        {
            throw AlreadyExists("identity_scope_exists", "An OIDC scope with this name already exists.");
        }

        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = normalizedName,
            DisplayName = NormalizeDisplayName(displayName),
            Description = NormalizeDescription(description),
        };
        descriptor.Resources.UnionWith(NormalizeResources(resources));
        var scope = await scopeManager.CreateAsync(descriptor, cancellationToken);
        return await ToManagedScopeAsync(scope, cancellationToken);
    }

    public async Task<ManagedOidcScope> UpdateScopeAsync(
        string scopeId,
        string displayName,
        string description,
        IEnumerable<string> resources,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = await RequireScopeByIdAsync(scopeId, cancellationToken);
        RequireVersion(GetScopeVersion(scope), expectedVersion);
        var current = await ToManagedScopeAsync(scope, cancellationToken);
        if (string.Equals(current.Name, ApiScope, StringComparison.Ordinal))
        {
            throw FailedPrecondition(
                "identity_scope_protected",
                "The built-in Asterloom API scope is configuration-managed.");
        }

        var descriptor = new OpenIddictScopeDescriptor();
        await scopeManager.PopulateAsync(descriptor, scope, cancellationToken);
        descriptor.DisplayName = NormalizeDisplayName(displayName);
        descriptor.Description = NormalizeDescription(description);
        descriptor.Resources.Clear();
        descriptor.Resources.UnionWith(NormalizeResources(resources));
        await scopeManager.UpdateAsync(scope, descriptor, cancellationToken);
        return await ToManagedScopeAsync(scope, cancellationToken);
    }

    public async Task<ManagedOidcScope> DeleteScopeAsync(
        string scopeId,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = await RequireScopeByIdAsync(scopeId, cancellationToken);
        RequireVersion(GetScopeVersion(scope), expectedVersion);
        var item = await ToManagedScopeAsync(scope, cancellationToken);
        if (string.Equals(item.Name, ApiScope, StringComparison.Ordinal))
        {
            throw FailedPrecondition(
                "identity_scope_protected",
                "The built-in Asterloom API scope cannot be deleted.");
        }

        await foreach (var application in applicationManager.ListAsync(cancellationToken: cancellationToken))
        {
            var permissions = await applicationManager.GetPermissionsAsync(application, cancellationToken);
            if (permissions.Contains(Permissions.Prefixes.Scope + item.Name, StringComparer.Ordinal))
            {
                throw FailedPrecondition(
                    "identity_scope_in_use",
                    "The scope is still assigned to one or more OIDC clients.");
            }
        }

        if (persistenceOptions.Provider == IdentityPersistenceProvider.Memory)
        {
            database.Remove(scope);
            await database.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await scopeManager.DeleteAsync(scope, cancellationToken);
        }
        return item;
    }

    private async Task<ManagedUserInvitation> CreateInvitationAsync(AsterloomUser user)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var invitationUri = new UriBuilder(new Uri(securityOptions.Issuer, "passport/invitation"))
        {
            Query = QueryString.Create(
                new Dictionary<string, string?>
                {
                    ["userId"] = user.Id.ToString("D", CultureInfo.InvariantCulture),
                    ["token"] = encodedToken,
                }).Value?.TrimStart('?'),
        }.Uri;
        return new ManagedUserInvitation(
            await ToManagedUserAsync(user),
            invitationUri.AbsoluteUri,
            timeProvider.GetUtcNow().Add(InvitationLifetime));
    }

    private async Task<ManagedIdentityUser> ToManagedUserAsync(AsterloomUser user) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.Status,
            user.Version,
            [.. (await userManager.GetRolesAsync(user)).Order(StringComparer.Ordinal)],
            user.CreatedAt,
            user.UpdatedAt,
            user.ArchivedAt);

    private async Task<ManagedIdentitySession> ToManagedSessionAsync(
        object authorization,
        Guid userId,
        bool revoked,
        CancellationToken cancellationToken)
    {
        var id = await authorizationManager.GetIdAsync(authorization, cancellationToken)
            ?? throw new InvalidOperationException("An OpenIddict authorization has no identifier.");
        var applicationId = await authorizationManager.GetApplicationIdAsync(
            authorization,
            cancellationToken);
        var application = string.IsNullOrWhiteSpace(applicationId)
            ? null
            : await applicationManager.FindByIdAsync(applicationId, cancellationToken);
        var clientId = application is null
            ? string.Empty
            : await applicationManager.GetClientIdAsync(application, cancellationToken)
                ?? string.Empty;
        var displayName = application is null
            ? clientId
            : await applicationManager.GetDisplayNameAsync(application, cancellationToken)
                ?? clientId;
        return new ManagedIdentitySession(
            id,
            userId,
            clientId,
            displayName,
            await authorizationManager.GetScopesAsync(authorization, cancellationToken),
            revoked,
            await authorizationManager.GetCreationDateAsync(authorization, cancellationToken)
                ?? DateTimeOffset.UnixEpoch);
    }

    private async Task<ManagedOidcClient> ToManagedClientAsync(
        object application,
        CancellationToken cancellationToken)
    {
        var permissions = await applicationManager.GetPermissionsAsync(
            application,
            cancellationToken);
        var grants = new List<ManagedOidcGrantType>(3);
        if (permissions.Contains(Permissions.GrantTypes.AuthorizationCode, StringComparer.Ordinal))
        {
            grants.Add(ManagedOidcGrantType.AuthorizationCode);
        }

        if (permissions.Contains(Permissions.GrantTypes.ClientCredentials, StringComparer.Ordinal))
        {
            grants.Add(ManagedOidcGrantType.ClientCredentials);
        }

        if (permissions.Contains(Permissions.GrantTypes.RefreshToken, StringComparer.Ordinal))
        {
            grants.Add(ManagedOidcGrantType.RefreshToken);
        }

        var scopes = permissions
            .Where(static permission => permission.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal))
            .Select(static permission => permission[Permissions.Prefixes.Scope.Length..])
            .ToHashSet(StringComparer.Ordinal);
        if (grants.Contains(ManagedOidcGrantType.AuthorizationCode))
        {
            scopes.Add(Scopes.OpenId);
        }

        if (grants.Contains(ManagedOidcGrantType.RefreshToken))
        {
            scopes.Add(Scopes.OfflineAccess);
        }

        return new ManagedOidcClient(
            await applicationManager.GetIdAsync(application, cancellationToken)
                ?? throw new InvalidOperationException("An OpenIddict application has no identifier."),
            await applicationManager.GetClientIdAsync(application, cancellationToken)
                ?? string.Empty,
            await applicationManager.GetDisplayNameAsync(application, cancellationToken)
                ?? string.Empty,
            await applicationManager.HasClientTypeAsync(
                application,
                ClientTypes.Confidential,
                cancellationToken)
                ? ManagedOidcClientType.Confidential
                : ManagedOidcClientType.Public,
            await applicationManager.HasApplicationTypeAsync(
                application,
                ApplicationTypes.Native,
                cancellationToken)
                ? ManagedOidcApplicationType.Native
                : ManagedOidcApplicationType.Web,
            grants,
            await applicationManager.GetRedirectUrisAsync(application, cancellationToken),
            await applicationManager.GetPostLogoutRedirectUrisAsync(application, cancellationToken),
            [.. scopes.Order(StringComparer.Ordinal)],
            GetApplicationVersion(application));
    }

    private async Task<ManagedOidcScope> ToManagedScopeAsync(
        object scope,
        CancellationToken cancellationToken) =>
        new(
            await scopeManager.GetIdAsync(scope, cancellationToken)
                ?? throw new InvalidOperationException("An OpenIddict scope has no identifier."),
            await scopeManager.GetNameAsync(scope, cancellationToken) ?? string.Empty,
            await scopeManager.GetDisplayNameAsync(scope, cancellationToken) ?? string.Empty,
            await scopeManager.GetDescriptionAsync(scope, cancellationToken) ?? string.Empty,
            await scopeManager.GetResourcesAsync(scope, cancellationToken),
            GetScopeVersion(scope));

    private async Task<AsterloomUser> RequireUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParse(userId, out var parsed) || parsed == Guid.Empty)
        {
            throw Invalid("userId", "A valid user identifier is required.");
        }

        return await userManager.FindByIdAsync(parsed.ToString("D", CultureInfo.InvariantCulture))
            ?? throw NotFound("identity_user_not_found", "The user was not found.");
    }

    private async Task<object> RequireClientAsync(
        string clientId,
        CancellationToken cancellationToken) =>
        await applicationManager.FindByClientIdAsync(
            NormalizeClientId(clientId),
            cancellationToken)
        ?? throw NotFound("identity_client_not_found", "The OIDC client was not found.");

    private async Task<object> RequireScopeByIdAsync(
        string scopeId,
        CancellationToken cancellationToken) =>
        await scopeManager.FindByIdAsync(
            RequireText(scopeId, "scopeId", 200),
            cancellationToken)
        ?? throw NotFound("identity_scope_not_found", "The OIDC scope was not found.");

    private async Task<long> RevokeUserSessionsCoreAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var subject = userId.ToString("D", CultureInfo.InvariantCulture);
        long valid = 0;
        var authorizations = await CollectAsync(
            authorizationManager.FindBySubjectAsync(subject, cancellationToken),
            cancellationToken);
        foreach (var authorization in authorizations)
        {
            if (await authorizationManager.HasStatusAsync(
                authorization,
                Statuses.Valid,
                cancellationToken))
            {
                valid++;
            }
        }

        await foreach (var authorization in authorizationManager
            .FindBySubjectAsync(subject, cancellationToken))
        {
            await authorizationManager.TryRevokeAsync(authorization, cancellationToken);
        }

        var tokens = await CollectAsync(
            tokenManager.FindBySubjectAsync(subject, cancellationToken),
            cancellationToken);
        foreach (var token in tokens)
        {
            await tokenManager.TryRevokeAsync(token, cancellationToken);
        }
        return valid;
    }

    private async Task EnsureNotLastSuperAdministratorAsync(AsterloomUser changingUser)
    {
        var administrators = await userManager.GetUsersInRoleAsync(
            IdentityRoleCatalog.SuperAdministrator);
        var activeOthers = administrators.Count(user =>
            user.Id != changingUser.Id
            && user.Status == AsterloomUserStatus.Active
            && user.ArchivedAt is null);
        if (activeOthers == 0)
        {
            throw FailedPrecondition(
                "identity_last_super_administrator",
                "The last active Super Administrator cannot be demoted, suspended, or archived.");
        }
    }

    private void RequireMutableClient(string clientId)
    {
        if (!string.IsNullOrWhiteSpace(bootstrapOptions.WebClientId)
            && string.Equals(
                clientId.Trim(),
                bootstrapOptions.WebClientId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw FailedPrecondition(
                "identity_client_protected",
                "The built-in Web client is configuration-managed.");
        }
    }

    private async Task UpdateUserRecordAsync(
        AsterloomUser user,
        bool rotateSecurityStamp = false)
    {
        if (rotateSecurityStamp)
        {
            EnsureSucceeded(
                await userManager.UpdateSecurityStampAsync(user),
                "identity_user_update_failed",
                "Unable to invalidate the user's sign-in state.");
        }

        EnsureSucceeded(
            await userManager.UpdateAsync(user),
            "identity_user_conflict",
            "The user was changed by another request.",
            conflict: true);
    }

    private void Touch(AsterloomUser user)
    {
        user.Version++;
        user.UpdatedAt = timeProvider.GetUtcNow();
    }

    private static OpenIddictApplicationDescriptor CreateClientDescriptor(
        string clientId,
        string displayName,
        ManagedOidcClientType clientType,
        ManagedOidcApplicationType applicationType,
        IReadOnlyList<ManagedOidcGrantType> grantTypes,
        IReadOnlyList<Uri> redirectUris,
        IReadOnlyList<Uri> postLogoutRedirectUris,
        IReadOnlyList<string> scopes,
        string secret)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = string.IsNullOrEmpty(secret) ? null : secret,
            ClientType = clientType == ManagedOidcClientType.Confidential
                ? ClientTypes.Confidential
                : ClientTypes.Public,
            ApplicationType = applicationType == ManagedOidcApplicationType.Native
                ? ApplicationTypes.Native
                : ApplicationTypes.Web,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = displayName,
        };
        ApplyClientConfiguration(
            descriptor,
            grantTypes,
            redirectUris,
            postLogoutRedirectUris,
            scopes);
        return descriptor;
    }

    private static void ApplyClientConfiguration(
        OpenIddictApplicationDescriptor descriptor,
        IReadOnlyList<ManagedOidcGrantType> grantTypes,
        IReadOnlyList<Uri> redirectUris,
        IReadOnlyList<Uri> postLogoutRedirectUris,
        IReadOnlyList<string> scopes)
    {
        descriptor.Permissions.Clear();
        descriptor.RedirectUris.Clear();
        descriptor.PostLogoutRedirectUris.Clear();
        descriptor.Requirements.Clear();
        if (grantTypes.Contains(ManagedOidcGrantType.AuthorizationCode))
        {
            descriptor.Permissions.UnionWith(
            [
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.EndSession,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code,
            ]);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        if (grantTypes.Contains(ManagedOidcGrantType.ClientCredentials))
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Token);
            descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        }

        if (grantTypes.Contains(ManagedOidcGrantType.RefreshToken))
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Token);
            descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        }

        foreach (var scope in scopes.Where(scope =>
            !string.Equals(scope, Scopes.OpenId, StringComparison.Ordinal)
            && !string.Equals(scope, Scopes.OfflineAccess, StringComparison.Ordinal)))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        descriptor.RedirectUris.UnionWith(redirectUris);
        descriptor.PostLogoutRedirectUris.UnionWith(postLogoutRedirectUris);
    }

    private async Task<IReadOnlyList<string>> NormalizeScopesAsync(
        IEnumerable<string> scopes,
        CancellationToken cancellationToken)
    {
        var normalized = scopes
            .Select(NormalizeScopeName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var scope in normalized)
        {
            if (StandardScopes.Contains(scope))
            {
                continue;
            }

            if (await scopeManager.FindByNameAsync(scope, cancellationToken) is null)
            {
                throw Invalid("scopes", $"The OIDC scope '{scope}' does not exist.");
            }
        }

        return normalized;
    }

    private static void ValidateClient(
        ManagedOidcClientType clientType,
        ManagedOidcApplicationType applicationType,
        ManagedOidcGrantType[] grants,
        Uri[] redirectUris)
    {
        if (grants.Length == 0)
        {
            throw Invalid("grantTypes", "At least one grant type is required.");
        }

        if (grants.Contains(ManagedOidcGrantType.AuthorizationCode)
            && redirectUris.Length == 0)
        {
            throw Invalid("redirectUris", "Authorization-code clients require a redirect URI.");
        }

        if (grants.Contains(ManagedOidcGrantType.ClientCredentials)
            && clientType != ManagedOidcClientType.Confidential)
        {
            throw Invalid("clientType", "Client credentials require a confidential client.");
        }

        if (applicationType == ManagedOidcApplicationType.Native
            && clientType != ManagedOidcClientType.Public)
        {
            throw Invalid(
                "clientType",
                "Native applications must use a public client with PKCE.");
        }

        if (applicationType == ManagedOidcApplicationType.Native
            && !grants.Contains(ManagedOidcGrantType.AuthorizationCode))
        {
            throw Invalid(
                "grantTypes",
                "Native applications require the authorization-code grant with PKCE.");
        }

        if (grants.Contains(ManagedOidcGrantType.RefreshToken)
            && !grants.Contains(ManagedOidcGrantType.AuthorizationCode))
        {
            throw Invalid("grantTypes", "Refresh tokens require the authorization-code grant.");
        }
    }

    private static ManagedOidcGrantType[] NormalizeGrantTypes(
        IEnumerable<ManagedOidcGrantType> grantTypes)
    {
        var values = grantTypes.Distinct().Order().ToArray();
        if (values.Any(value => !Enum.IsDefined(value)))
        {
            throw Invalid("grantTypes", "An unsupported grant type was supplied.");
        }

        return values;
    }

    private static Uri[] NormalizeUris(
        IEnumerable<string> values,
        string field,
        ManagedOidcApplicationType applicationType)
    {
        var uris = new List<Uri>();
        foreach (var value in values)
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw Invalid(field, "Every URI must be absolute and must not contain a fragment.");
            }

            var isHttp = string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase);
            var isHttps = string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);
            if (applicationType == ManagedOidcApplicationType.Web
                && !isHttp
                && !isHttps)
            {
                throw Invalid(field, "Web application redirects must use HTTP or HTTPS.");
            }

            if (isHttp && !uri.IsLoopback)
            {
                throw Invalid(field, "Plain HTTP is permitted only for loopback redirect URIs.");
            }

            if (applicationType == ManagedOidcApplicationType.Native
                && !isHttp
                && !isHttps
                && !uri.Scheme.Contains('.', StringComparison.Ordinal))
            {
                throw Invalid(
                    field,
                    "Native custom URI schemes must use a reverse-domain name such as com.example.app.");
            }

            uris.Add(uri);
        }

        return uris.DistinctBy(static uri => uri.AbsoluteUri).ToArray();
    }

    private static string[] NormalizeResources(IEnumerable<string> resources) =>
        resources
            .Select(resource => RequireText(resource, "resources", 200))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] NormalizeRoles(IEnumerable<string> roles)
    {
        var normalized = roles
            .Select(role => RequireText(role, "roles", 100))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw Invalid("roles", "At least one role is required.");
        }

        var unknown = normalized.FirstOrDefault(role => !IdentityRoleCatalog.IsKnown(role));
        if (unknown is not null)
        {
            throw Invalid("roles", $"The role '{unknown}' is not a trusted Passport role.");
        }

        return normalized;
    }

    private static string NormalizeEmail(string value)
    {
        var email = RequireText(value, "email", 320).ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out var address)
            || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("email", "A valid email address is required.");
        }

        return email;
    }

    private static string NormalizeClientId(string value)
    {
        var clientId = RequireText(value, "clientId", 100).ToLowerInvariant();
        if (!ClientIdPattern().IsMatch(clientId))
        {
            throw Invalid(
                "clientId",
                "Client IDs must start with a letter and contain only lowercase letters, digits, '.', '_' or '-'.");
        }

        return clientId;
    }

    private static string NormalizeScopeName(string value)
    {
        var name = RequireText(value, "name", 100).ToLowerInvariant();
        if (!ScopeNamePattern().IsMatch(name))
        {
            throw Invalid(
                "name",
                "Scope names must start with a letter and contain only lowercase letters, digits, '.', '_', ':' or '-'.");
        }

        return name;
    }

    private static string NormalizeDisplayName(string value) =>
        RequireText(value, "displayName", 200);

    private static string NormalizeDescription(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 1_000)
        {
            throw Invalid("description", "Description cannot exceed 1,000 characters.");
        }

        return normalized;
    }

    private static string RequireText(string? value, string field, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw Invalid(field, $"A non-empty value up to {maximumLength} characters is required.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GenerateSecret() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));

    private static string GetApplicationVersion(object application) =>
        application is OpenIddictEntityFrameworkCoreApplication entity
            ? entity.ConcurrencyToken ?? string.Empty
            : throw new InvalidOperationException("Unsupported OpenIddict application entity type.");

    private static string GetScopeVersion(object scope) =>
        scope is OpenIddictEntityFrameworkCoreScope entity
            ? entity.ConcurrencyToken ?? string.Empty
            : throw new InvalidOperationException("Unsupported OpenIddict scope entity type.");

    private static void RequireVersion(long current, long expected)
    {
        if (expected <= 0 || current != expected)
        {
            throw Conflict("identity_version_conflict", "The resource was changed by another request.");
        }
    }

    private static void RequireVersion(string current, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected)
            || !string.Equals(current, expected, StringComparison.Ordinal))
        {
            throw Conflict("identity_version_conflict", "The resource was changed by another request.");
        }
    }

    private static void RequireNotArchived(AsterloomUser user)
    {
        if (user.Status == AsterloomUserStatus.Archived)
        {
            throw FailedPrecondition(
                "identity_user_archived",
                "Restore the archived user before changing it.");
        }
    }

    private static PageRequest ParsePage(int pageSize, string? pageToken)
    {
        var size = pageSize <= 0 ? DefaultPageSize : pageSize;
        if (size > MaximumPageSize)
        {
            throw Invalid("pageSize", $"Page size cannot exceed {MaximumPageSize}.");
        }

        if (string.IsNullOrWhiteSpace(pageToken))
        {
            return new PageRequest(size, 0);
        }

        try
        {
            var text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(pageToken));
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                && offset >= 0
                ? new PageRequest(size, offset)
                : throw Invalid("pageToken", "The page token is invalid.");
        }
        catch (FormatException)
        {
            throw Invalid("pageToken", "The page token is invalid.");
        }
    }

    private static string EncodePageToken(int offset) =>
        WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    private static async Task<List<object>> CollectAsync(
        IAsyncEnumerable<object> source,
        CancellationToken cancellationToken)
    {
        var items = new List<object>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        string errorCode,
        string message,
        bool conflict = false)
    {
        if (result.Succeeded)
        {
            return;
        }

        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["identity"] = result.Errors
                .Select(static error => error.Description)
                .ToArray(),
        };
        throw new AsterloomException(
            conflict ? AsterloomErrorKind.Conflict : AsterloomErrorKind.InvalidArgument,
            errorCode,
            message,
            fields);
    }

    private static AsterloomException Invalid(string field, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "validation_failed",
        "One or more fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        });

    private static AsterloomException NotFound(string code, string message) =>
        new(AsterloomErrorKind.NotFound, code, message);

    private static AsterloomException AlreadyExists(string code, string message) =>
        new(AsterloomErrorKind.AlreadyExists, code, message);

    private static AsterloomException Conflict(string code, string message) =>
        new(AsterloomErrorKind.Conflict, code, message);

    private static AsterloomException FailedPrecondition(string code, string message) =>
        new(AsterloomErrorKind.FailedPrecondition, code, message);

    [GeneratedRegex("^[a-z][a-z0-9._-]{1,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdPattern();

    [GeneratedRegex("^[a-z][a-z0-9._:-]{1,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopeNamePattern();

    private sealed record PageRequest(int Size, int Offset);
}
