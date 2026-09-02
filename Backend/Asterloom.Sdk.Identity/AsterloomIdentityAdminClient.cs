using System.Net.Mail;
using Asterloom.Protocol.Identity.Admin.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProtocolApplicationType = Asterloom.Protocol.Identity.V1.OidcApplicationType;
using ProtocolClient = Asterloom.Protocol.Identity.V1.OidcClient;
using ProtocolClientCredential = Asterloom.Protocol.Identity.V1.OidcClientCredential;
using ProtocolClientType = Asterloom.Protocol.Identity.V1.OidcClientType;
using ProtocolGrantType = Asterloom.Protocol.Identity.V1.OidcGrantType;
using ProtocolScope = Asterloom.Protocol.Identity.V1.OidcScope;
using ProtocolSession = Asterloom.Protocol.Identity.V1.IdentitySession;
using ProtocolSessionStatus = Asterloom.Protocol.Identity.V1.IdentitySessionStatus;
using ProtocolUser = Asterloom.Protocol.Identity.V1.IdentityUser;
using ProtocolUserInvitation = Asterloom.Protocol.Identity.V1.UserInvitation;
using ProtocolUserStatus = Asterloom.Protocol.Identity.V1.IdentityUserStatus;
using ProtocolMembership = Asterloom.Protocol.Identity.V1.ApplicationMembership;
using ProtocolMembershipStatus = Asterloom.Protocol.Identity.V1.ApplicationMembershipStatus;

namespace Asterloom.Sdk.Identity;

public sealed class AsterloomIdentityAdminClient
{
    private const int MaximumPageSize = 100;

    private readonly IdentityAdminService.IdentityAdminServiceClient _client;

    public AsterloomIdentityAdminClient(CallInvoker callInvoker)
        : this(new IdentityAdminService.IdentityAdminServiceClient(
            callInvoker ?? throw new ArgumentNullException(nameof(callInvoker))))
    {
    }

    public AsterloomIdentityAdminClient(
        IdentityAdminService.IdentityAdminServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<AsterloomIdentityPage<AsterloomIdentityUser>> ListUsersAsync(
        string? query = null,
        bool includeArchived = false,
        int pageSize = MaximumPageSize,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.ListUsersAsync(
            new ListUsersRequest
            {
                IncludeArchived = includeArchived,
                PageSize = ValidatePageSize(pageSize),
                PageToken = NormalizeOptional(pageToken, 2_048),
                Query = NormalizeOptional(query, 200),
            },
            cancellationToken: cancellationToken);
        return new(
            response.Users.Select(ToModel).ToArray(),
            EmptyToNull(response.NextPageToken));
    }

    public async Task<AsterloomIdentityUser> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        ToModel(await _client.GetUserAsync(
            new GetUserRequest { UserId = FormatId(userId, nameof(userId)) },
            cancellationToken: cancellationToken));

    public async Task<AsterloomIdentityUser> CreateUserAsync(
        AsterloomIdentityUserRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var request = new CreateUserRequest
        {
            Email = ValidateEmail(registration.Email),
            DisplayName = RequireText(
                registration.DisplayName,
                nameof(registration.DisplayName),
                200),
            Password = RequireSecret(
                registration.Password,
                nameof(registration.Password),
                2_048),
            EmailConfirmed = registration.EmailConfirmed,
        };
        request.Roles.AddRange(ValidateRoles(registration.Roles, requireAny: false));
        return ToModel(await _client.CreateUserAsync(
            request,
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomUserInvitation> InviteUserAsync(
        string email,
        string displayName,
        IEnumerable<AsterloomPassportRole> roles,
        CancellationToken cancellationToken = default)
    {
        var request = new InviteUserRequest
        {
            Email = ValidateEmail(email),
            DisplayName = RequireText(displayName, nameof(displayName), 200),
        };
        request.Roles.AddRange(ValidateRoles(roles));
        return ToModel(await _client.InviteUserAsync(
            request,
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomUserInvitation> ResendUserInvitationAsync(
        AsterloomIdentityUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return ToModel(await _client.ResendUserInvitationAsync(
            new ResendUserInvitationRequest
            {
                UserId = FormatId(user.Id, nameof(user)),
                ExpectedVersion = ValidateVersion(user.Version, nameof(user)),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomIdentityUser> UpdateUserAsync(
        AsterloomIdentityUser user,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return ToModel(await _client.UpdateUserAsync(
            new UpdateUserRequest
            {
                UserId = FormatId(user.Id, nameof(user)),
                DisplayName = RequireText(displayName, nameof(displayName), 200),
                ExpectedVersion = ValidateVersion(user.Version, nameof(user)),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomIdentityUser> SetUserRolesAsync(
        AsterloomIdentityUser user,
        IEnumerable<AsterloomPassportRole> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var request = new SetUserRolesRequest
        {
            UserId = FormatId(user.Id, nameof(user)),
            ExpectedVersion = ValidateVersion(user.Version, nameof(user)),
        };
        request.Roles.AddRange(ValidateRoles(roles, requireAny: false));
        return ToModel(await _client.SetUserRolesAsync(
            request,
            cancellationToken: cancellationToken));
    }

    public Task<AsterloomIdentityUser> SuspendUserAsync(
        AsterloomIdentityUser user,
        CancellationToken cancellationToken = default) =>
        ChangeUserStatusAsync(user, UserLifecycleAction.Suspend, cancellationToken);

    public Task<AsterloomIdentityUser> ReactivateUserAsync(
        AsterloomIdentityUser user,
        CancellationToken cancellationToken = default) =>
        ChangeUserStatusAsync(user, UserLifecycleAction.Reactivate, cancellationToken);

    public Task<AsterloomIdentityUser> ArchiveUserAsync(
        AsterloomIdentityUser user,
        CancellationToken cancellationToken = default) =>
        ChangeUserStatusAsync(user, UserLifecycleAction.Archive, cancellationToken);

    public Task<AsterloomIdentityUser> RestoreUserAsync(
        AsterloomIdentityUser user,
        CancellationToken cancellationToken = default) =>
        ChangeUserStatusAsync(user, UserLifecycleAction.Restore, cancellationToken);

    public async Task<AsterloomIdentityUser> ResetUserPasswordAsync(
        AsterloomIdentityUser user,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return ToModel(await _client.ResetUserPasswordAsync(
            new ResetUserPasswordRequest
            {
                UserId = FormatId(user.Id, nameof(user)),
                NewPassword = RequireSecret(newPassword, nameof(newPassword), 2_048),
                ExpectedVersion = ValidateVersion(user.Version, nameof(user)),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomIdentityPage<AsterloomApplicationMembership>>
        ListApplicationMembershipsAsync(
            Guid? userId = null,
            Guid? tenantId = null,
            Guid? applicationId = null,
            bool includeRemoved = false,
            int pageSize = MaximumPageSize,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
    {
        var response = await _client.ListApplicationMembershipsAsync(
            new ListApplicationMembershipsRequest
            {
                UserId = userId is null ? string.Empty : FormatId(userId.Value, nameof(userId)),
                TenantId = tenantId is null
                    ? string.Empty
                    : FormatId(tenantId.Value, nameof(tenantId)),
                ApplicationId = applicationId is null
                    ? string.Empty
                    : FormatId(applicationId.Value, nameof(applicationId)),
                IncludeRemoved = includeRemoved,
                PageSize = ValidatePageSize(pageSize),
                PageToken = NormalizeOptional(pageToken, 2_048),
            },
            cancellationToken: cancellationToken);
        return new(
            response.Memberships.Select(ToModel).ToArray(),
            EmptyToNull(response.NextPageToken));
    }

    public async Task<AsterloomApplicationMembership> SetApplicationMembershipAsync(
        Guid userId,
        Guid tenantId,
        Guid applicationId,
        long expectedVersion = 0,
        CancellationToken cancellationToken = default) =>
        ToModel(await _client.SetApplicationMembershipAsync(
            new SetApplicationMembershipRequest
            {
                UserId = FormatId(userId, nameof(userId)),
                TenantId = FormatId(tenantId, nameof(tenantId)),
                ApplicationId = FormatId(applicationId, nameof(applicationId)),
                ExpectedVersion = expectedVersion,
            },
            cancellationToken: cancellationToken));

    public async Task<AsterloomApplicationMembership> RemoveApplicationMembershipAsync(
        AsterloomApplicationMembership membership,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);
        return ToModel(await _client.RemoveApplicationMembershipAsync(
            new RemoveApplicationMembershipRequest
            {
                UserId = FormatId(membership.UserId, nameof(membership)),
                ApplicationId = FormatId(membership.ApplicationId, nameof(membership)),
                ExpectedVersion = ValidateVersion(membership.Version, nameof(membership)),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomIdentityPage<AsterloomIdentitySession>> ListUserSessionsAsync(
        Guid userId,
        bool includeRevoked = true,
        int pageSize = MaximumPageSize,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.ListUserSessionsAsync(
            new ListUserSessionsRequest
            {
                UserId = FormatId(userId, nameof(userId)),
                IncludeRevoked = includeRevoked,
                PageSize = ValidatePageSize(pageSize),
                PageToken = NormalizeOptional(pageToken, 2_048),
            },
            cancellationToken: cancellationToken);
        return new(
            response.Sessions.Select(ToModel).ToArray(),
            EmptyToNull(response.NextPageToken));
    }

    public async Task<AsterloomIdentitySession> RevokeUserSessionAsync(
        Guid userId,
        string sessionId,
        CancellationToken cancellationToken = default) =>
        ToModel(await _client.RevokeUserSessionAsync(
            new RevokeUserSessionRequest
            {
                UserId = FormatId(userId, nameof(userId)),
                SessionId = RequireText(sessionId, nameof(sessionId), 200),
            },
            cancellationToken: cancellationToken));

    public async Task<long> RevokeAllUserSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        (await _client.RevokeAllUserSessionsAsync(
            new RevokeAllUserSessionsRequest
            {
                UserId = FormatId(userId, nameof(userId)),
            },
            cancellationToken: cancellationToken)).RevokedSessions;

    public async Task<AsterloomIdentityPage<AsterloomOidcClient>> ListClientsAsync(
        string? query = null,
        int pageSize = MaximumPageSize,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.ListClientsAsync(
            new ListClientsRequest
            {
                PageSize = ValidatePageSize(pageSize),
                PageToken = NormalizeOptional(pageToken, 2_048),
                Query = NormalizeOptional(query, 200),
            },
            cancellationToken: cancellationToken);
        return new(
            response.Clients.Select(ToModel).ToArray(),
            EmptyToNull(response.NextPageToken));
    }

    public async Task<AsterloomOidcClient> GetClientAsync(
        string clientId,
        CancellationToken cancellationToken = default) =>
        ToModel(await _client.GetClientAsync(
            new GetClientRequest
            {
                ClientId = RequireText(clientId, nameof(clientId), 100).ToLowerInvariant(),
            },
            cancellationToken: cancellationToken));

    public async Task<AsterloomOidcClientCredential> CreateClientAsync(
        AsterloomOidcClientRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateClientRegistration(registration);
        var request = new CreateClientRequest
        {
            ClientId = RequireText(
                registration.ClientId,
                nameof(registration.ClientId),
                100).ToLowerInvariant(),
            DisplayName = RequireText(
                registration.DisplayName,
                nameof(registration.DisplayName),
                200),
            ApplicationType = ToProtocol(registration.ApplicationType),
            ClientType = ToProtocol(registration.ClientType),
            TenantId = registration.TenantId?.ToString("D") ?? string.Empty,
            ApplicationId = registration.ApplicationId?.ToString("D") ?? string.Empty,
            AllowUserRegistration = registration.AllowUserRegistration,
            AllowMembershipAutoJoin = registration.AllowMembershipAutoJoin,
        };
        request.GrantTypes.AddRange(registration.GrantTypes.Select(ToProtocol));
        request.RedirectUris.AddRange(registration.RedirectUris.Select(ToProtocolUri));
        request.PostLogoutRedirectUris.AddRange(
            registration.PostLogoutRedirectUris.Select(ToProtocolUri));
        request.Scopes.AddRange(NormalizeValues(registration.Scopes, nameof(registration.Scopes), 100));
        return ToModel(await _client.CreateClientAsync(
            request,
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomOidcClient> UpdateClientAsync(
        AsterloomOidcClient client,
        AsterloomOidcClientUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(update);
        ValidateClientUpdate(client, update);
        var request = new UpdateClientRequest
        {
            ClientId = RequireText(client.ClientId, nameof(client), 100),
            DisplayName = RequireText(update.DisplayName, nameof(update.DisplayName), 200),
            ExpectedVersion = RequireText(client.Version, nameof(client), 200),
            TenantId = update.TenantId?.ToString("D") ?? string.Empty,
            ApplicationId = update.ApplicationId?.ToString("D") ?? string.Empty,
            AllowUserRegistration = update.AllowUserRegistration,
            AllowMembershipAutoJoin = update.AllowMembershipAutoJoin,
        };
        request.GrantTypes.AddRange(update.GrantTypes.Select(ToProtocol));
        request.RedirectUris.AddRange(update.RedirectUris.Select(ToProtocolUri));
        request.PostLogoutRedirectUris.AddRange(
            update.PostLogoutRedirectUris.Select(ToProtocolUri));
        request.Scopes.AddRange(NormalizeValues(update.Scopes, nameof(update.Scopes), 100));
        return ToModel(await _client.UpdateClientAsync(
            request,
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomOidcClientCredential> RotateClientSecretAsync(
        AsterloomOidcClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return ToModel(await _client.RotateClientSecretAsync(
            new RotateClientSecretRequest
            {
                ClientId = RequireText(client.ClientId, nameof(client), 100),
                ExpectedVersion = RequireText(client.Version, nameof(client), 200),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomOidcClient> DeleteClientAsync(
        AsterloomOidcClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return ToModel(await _client.DeleteClientAsync(
            new DeleteClientRequest
            {
                ClientId = RequireText(client.ClientId, nameof(client), 100),
                ExpectedVersion = RequireText(client.Version, nameof(client), 200),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomIdentityPage<AsterloomOidcScope>> ListScopesAsync(
        string? query = null,
        int pageSize = MaximumPageSize,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.ListScopesAsync(
            new ListScopesRequest
            {
                PageSize = ValidatePageSize(pageSize),
                PageToken = NormalizeOptional(pageToken, 2_048),
                Query = NormalizeOptional(query, 200),
            },
            cancellationToken: cancellationToken);
        return new(
            response.Scopes.Select(ToModel).ToArray(),
            EmptyToNull(response.NextPageToken));
    }

    public async Task<AsterloomOidcScope> GetScopeAsync(
        string scopeId,
        CancellationToken cancellationToken = default) =>
        ToModel(await _client.GetScopeAsync(
            new GetScopeRequest
            {
                ScopeId = RequireText(scopeId, nameof(scopeId), 200),
            },
            cancellationToken: cancellationToken));

    public async Task<AsterloomOidcScope> CreateScopeAsync(
        AsterloomOidcScopeRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var request = new CreateScopeRequest
        {
            Name = RequireText(registration.Name, nameof(registration.Name), 100).ToLowerInvariant(),
            DisplayName = RequireText(
                registration.DisplayName,
                nameof(registration.DisplayName),
                200),
            Description = NormalizeOptional(registration.Description, 1_000),
        };
        request.Resources.AddRange(
            NormalizeValues(registration.Resources, nameof(registration.Resources), 200));
        return ToModel(await _client.CreateScopeAsync(
            request,
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomOidcScope> UpdateScopeAsync(
        AsterloomOidcScope scope,
        AsterloomOidcScopeUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(update);
        var request = new UpdateScopeRequest
        {
            ScopeId = RequireText(scope.Id, nameof(scope), 200),
            DisplayName = RequireText(update.DisplayName, nameof(update.DisplayName), 200),
            Description = NormalizeOptional(update.Description, 1_000),
            ExpectedVersion = RequireText(scope.Version, nameof(scope), 200),
        };
        request.Resources.AddRange(
            NormalizeValues(update.Resources, nameof(update.Resources), 200));
        return ToModel(await _client.UpdateScopeAsync(
            request,
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomOidcScope> DeleteScopeAsync(
        AsterloomOidcScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ToModel(await _client.DeleteScopeAsync(
            new DeleteScopeRequest
            {
                ScopeId = RequireText(scope.Id, nameof(scope), 200),
                ExpectedVersion = RequireText(scope.Version, nameof(scope), 200),
            },
            cancellationToken: cancellationToken));
    }

    private async Task<AsterloomIdentityUser> ChangeUserStatusAsync(
        AsterloomIdentityUser user,
        UserLifecycleAction action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var request = new UserVersionRequest
        {
            UserId = FormatId(user.Id, nameof(user)),
            ExpectedVersion = ValidateVersion(user.Version, nameof(user)),
        };
        var response = action switch
        {
            UserLifecycleAction.Suspend => await _client.SuspendUserAsync(
                request,
                cancellationToken: cancellationToken),
            UserLifecycleAction.Reactivate => await _client.ReactivateUserAsync(
                request,
                cancellationToken: cancellationToken),
            UserLifecycleAction.Archive => await _client.ArchiveUserAsync(
                request,
                cancellationToken: cancellationToken),
            UserLifecycleAction.Restore => await _client.RestoreUserAsync(
                request,
                cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
        return ToModel(response);
    }

    private static AsterloomIdentityUser ToModel(ProtocolUser user) => new(
        ParseGuid(user.Id, "user.id"),
        user.Email,
        user.DisplayName,
        user.Status switch
        {
            ProtocolUserStatus.Pending => AsterloomIdentityUserStatus.Pending,
            ProtocolUserStatus.Active => AsterloomIdentityUserStatus.Active,
            ProtocolUserStatus.Suspended => AsterloomIdentityUserStatus.Suspended,
            ProtocolUserStatus.Archived => AsterloomIdentityUserStatus.Archived,
            _ => throw InvalidProtocol("identity user status"),
        },
        user.Version,
        user.Roles.Select(ToRole).ToArray(),
        ToDateTimeOffset(user.CreatedAt, "user.created_at"),
        ToDateTimeOffset(user.UpdatedAt, "user.updated_at"),
        user.ArchivedAt is null ? null : user.ArchivedAt.ToDateTimeOffset(),
        user.EmailConfirmed);

    private static AsterloomApplicationMembership ToModel(
        ProtocolMembership membership) => new(
        ParseGuid(membership.UserId, "membership.user_id"),
        ParseGuid(membership.TenantId, "membership.tenant_id"),
        ParseGuid(membership.ApplicationId, "membership.application_id"),
        membership.Status switch
        {
            ProtocolMembershipStatus.Active => AsterloomApplicationMembershipStatus.Active,
            ProtocolMembershipStatus.Removed => AsterloomApplicationMembershipStatus.Removed,
            _ => throw InvalidProtocol("application membership status"),
        },
        membership.Version,
        ToDateTimeOffset(membership.CreatedAt, "membership.created_at"),
        ToDateTimeOffset(membership.UpdatedAt, "membership.updated_at"));

    private static AsterloomUserInvitation ToModel(ProtocolUserInvitation invitation) => new(
        ToModel(invitation.User ?? throw InvalidProtocol("invitation user")),
        new Uri(invitation.InvitationUrl, UriKind.Absolute),
        ToDateTimeOffset(invitation.ExpiresAt, "invitation.expires_at"));

    private static AsterloomIdentitySession ToModel(ProtocolSession session) => new(
        session.Id,
        ParseGuid(session.UserId, "session.user_id"),
        session.ClientId,
        session.ClientDisplayName,
        session.Scopes.ToArray(),
        session.Status switch
        {
            ProtocolSessionStatus.Valid => AsterloomIdentitySessionStatus.Valid,
            ProtocolSessionStatus.Revoked => AsterloomIdentitySessionStatus.Revoked,
            _ => throw InvalidProtocol("identity session status"),
        },
        ToDateTimeOffset(session.CreatedAt, "session.created_at"));

    private static AsterloomOidcClient ToModel(ProtocolClient client) => new(
        client.Id,
        client.ClientId,
        client.DisplayName,
        client.ApplicationType switch
        {
            ProtocolApplicationType.Web => AsterloomOidcApplicationType.Web,
            ProtocolApplicationType.Native => AsterloomOidcApplicationType.Native,
            _ => throw InvalidProtocol("OIDC application type"),
        },
        client.ClientType switch
        {
            ProtocolClientType.Public => AsterloomOidcClientType.Public,
            ProtocolClientType.Confidential => AsterloomOidcClientType.Confidential,
            _ => throw InvalidProtocol("OIDC client type"),
        },
        client.GrantTypes.Select(ToModel).ToArray(),
        client.RedirectUris.Select(ToAbsoluteUri).ToArray(),
        client.PostLogoutRedirectUris.Select(ToAbsoluteUri).ToArray(),
        client.Scopes.ToArray(),
        client.Version,
        ParseOptionalGuid(client.TenantId, "client.tenant_id"),
        ParseOptionalGuid(client.ApplicationId, "client.application_id"),
        client.AllowUserRegistration,
        client.AllowMembershipAutoJoin,
        client.IsSystem,
        client.IsMutable);

    private static AsterloomOidcClientCredential ToModel(
        ProtocolClientCredential credential) => new(
        ToModel(credential.Client ?? throw InvalidProtocol("OIDC client credential")),
        credential.ClientSecret);

    private static AsterloomOidcScope ToModel(ProtocolScope scope) => new(
        scope.Id,
        scope.Name,
        scope.DisplayName,
        scope.Description,
        scope.Resources.ToArray(),
        scope.Version,
        scope.IsSystem,
        scope.IsMutable);

    private static AsterloomOidcGrantType ToModel(ProtocolGrantType type) => type switch
    {
        ProtocolGrantType.AuthorizationCode => AsterloomOidcGrantType.AuthorizationCode,
        ProtocolGrantType.ClientCredentials => AsterloomOidcGrantType.ClientCredentials,
        ProtocolGrantType.RefreshToken => AsterloomOidcGrantType.RefreshToken,
        _ => throw InvalidProtocol("OIDC grant type"),
    };

    private static ProtocolApplicationType ToProtocol(AsterloomOidcApplicationType type) =>
        type switch
        {
            AsterloomOidcApplicationType.Web => ProtocolApplicationType.Web,
            AsterloomOidcApplicationType.Native => ProtocolApplicationType.Native,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

    private static ProtocolClientType ToProtocol(AsterloomOidcClientType type) => type switch
    {
        AsterloomOidcClientType.Public => ProtocolClientType.Public,
        AsterloomOidcClientType.Confidential => ProtocolClientType.Confidential,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static ProtocolGrantType ToProtocol(AsterloomOidcGrantType type) => type switch
    {
        AsterloomOidcGrantType.AuthorizationCode => ProtocolGrantType.AuthorizationCode,
        AsterloomOidcGrantType.ClientCredentials => ProtocolGrantType.ClientCredentials,
        AsterloomOidcGrantType.RefreshToken => ProtocolGrantType.RefreshToken,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static AsterloomPassportRole ToRole(string role) => role switch
    {
        "SuperAdministrator" => AsterloomPassportRole.SuperAdministrator,
        "TenantAdministrator" => AsterloomPassportRole.TenantAdministrator,
        "Operator" => AsterloomPassportRole.Operator,
        "Developer" => AsterloomPassportRole.Developer,
        "Viewer" => AsterloomPassportRole.Viewer,
        _ => throw InvalidProtocol($"Passport role '{role}'"),
    };

    private static string ToProtocol(AsterloomPassportRole role) => role switch
    {
        AsterloomPassportRole.SuperAdministrator => "SuperAdministrator",
        AsterloomPassportRole.TenantAdministrator => "TenantAdministrator",
        AsterloomPassportRole.Operator => "Operator",
        AsterloomPassportRole.Developer => "Developer",
        AsterloomPassportRole.Viewer => "Viewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    private static void ValidateClientRegistration(AsterloomOidcClientRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration.GrantTypes);
        ArgumentNullException.ThrowIfNull(registration.RedirectUris);
        ArgumentNullException.ThrowIfNull(registration.PostLogoutRedirectUris);
        ArgumentNullException.ThrowIfNull(registration.Scopes);
        ValidateClientConfiguration(
            registration.ApplicationType,
            registration.ClientType,
            registration.GrantTypes,
            registration.RedirectUris,
            registration.TenantId,
            registration.ApplicationId,
            registration.AllowUserRegistration,
            registration.AllowMembershipAutoJoin);
    }

    private static void ValidateClientUpdate(
        AsterloomOidcClient client,
        AsterloomOidcClientUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update.GrantTypes);
        ArgumentNullException.ThrowIfNull(update.RedirectUris);
        ArgumentNullException.ThrowIfNull(update.PostLogoutRedirectUris);
        ArgumentNullException.ThrowIfNull(update.Scopes);
        ValidateClientConfiguration(
            client.ApplicationType,
            client.ClientType,
            update.GrantTypes,
            update.RedirectUris,
            update.TenantId,
            update.ApplicationId,
            update.AllowUserRegistration,
            update.AllowMembershipAutoJoin);
    }

    private static void ValidateClientConfiguration(
        AsterloomOidcApplicationType applicationType,
        AsterloomOidcClientType clientType,
        IReadOnlyCollection<AsterloomOidcGrantType> grants,
        IReadOnlyCollection<Uri> redirectUris,
        Guid? tenantId,
        Guid? applicationId,
        bool allowUserRegistration,
        bool allowMembershipAutoJoin)
    {
        if (grants.Count == 0)
        {
            throw new ArgumentException("At least one grant type is required.", nameof(grants));
        }

        if (grants.Contains(AsterloomOidcGrantType.AuthorizationCode)
            && redirectUris.Count == 0)
        {
            throw new ArgumentException(
                "Authorization-code clients require a redirect URI.",
                nameof(redirectUris));
        }

        if (grants.Contains(AsterloomOidcGrantType.ClientCredentials)
            && clientType != AsterloomOidcClientType.Confidential)
        {
            throw new ArgumentException(
                "Client credentials require a confidential client.",
                nameof(clientType));
        }

        if (tenantId.HasValue != applicationId.HasValue)
        {
            throw new ArgumentException(
                "Tenant and application identifiers must be supplied together.",
                nameof(applicationId));
        }

        if ((allowUserRegistration || allowMembershipAutoJoin)
            && applicationId is null)
        {
            throw new ArgumentException(
                "Application capabilities require a platform application binding.",
                nameof(applicationId));
        }

        if (allowUserRegistration
            && !grants.Contains(AsterloomOidcGrantType.ClientCredentials))
        {
            throw new ArgumentException(
                "Account registration requires client credentials.",
                nameof(allowUserRegistration));
        }

        if (applicationType == AsterloomOidcApplicationType.Native
            && (clientType != AsterloomOidcClientType.Public
                || !grants.Contains(AsterloomOidcGrantType.AuthorizationCode)))
        {
            throw new ArgumentException(
                "Native applications must use a public authorization-code client with PKCE.",
                nameof(applicationType));
        }

        if (grants.Contains(AsterloomOidcGrantType.RefreshToken)
            && !grants.Contains(AsterloomOidcGrantType.AuthorizationCode))
        {
            throw new ArgumentException(
                "Refresh tokens require authorization-code authentication.",
                nameof(grants));
        }
    }

    private static string[] ValidateRoles(
        IEnumerable<AsterloomPassportRole> roles,
        bool requireAny = true)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var values = roles.Distinct().Order().Select(ToProtocol).ToArray();
        if (requireAny && values.Length == 0)
        {
            throw new ArgumentException("At least one role is required.", nameof(roles));
        }

        return values;
    }

    private static string[] NormalizeValues(
        IEnumerable<string> values,
        string parameterName,
        int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values
            .Select(value => RequireText(value, parameterName, maximumLength))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ValidateEmail(string value)
    {
        var email = RequireText(value, nameof(value), 320).ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out var parsed)
            || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A valid email address is required.", nameof(value));
        }

        return email;
    }

    private static string RequireText(string? value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A non-empty value of at most {maximumLength} characters is required.",
                parameterName);
        }

        return normalized;
    }

    private static string RequireSecret(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A non-empty value of at most {maximumLength} characters is required.",
                parameterName);
        }

        return value;
    }

    private static string NormalizeOptional(string? value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                normalized.Length,
                $"The value cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static int ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                pageSize,
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        return pageSize;
    }

    private static long ValidateVersion(long version, string parameterName)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                version,
                "The resource version must be positive.");
        }

        return version;
    }

    private static string FormatId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The identifier cannot be empty.", parameterName);
        }

        return value.ToString("D");
    }

    private static Guid ParseGuid(string value, string field) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw InvalidProtocol(field);

    private static Guid? ParseOptionalGuid(string value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseGuid(value, field);

    private static DateTimeOffset ToDateTimeOffset(Timestamp? value, string field) =>
        value?.ToDateTimeOffset() ?? throw InvalidProtocol(field);

    private static Uri ToAbsoluteUri(string value) => new(value, UriKind.Absolute);

    private static string ToProtocolUri(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.IsAbsoluteUri || !string.IsNullOrEmpty(value.Fragment))
        {
            throw new ArgumentException(
                "Redirect URIs must be absolute and must not contain a fragment.",
                nameof(value));
        }

        return value.AbsoluteUri;
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static InvalidDataException InvalidProtocol(string field) =>
        new($"The Identity service returned an invalid {field} value.");

    private enum UserLifecycleAction
    {
        Suspend,
        Reactivate,
        Archive,
        Restore,
    }
}
