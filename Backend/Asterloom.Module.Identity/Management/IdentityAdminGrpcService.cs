using Asterloom.Protocol.Identity.Admin.V1;
using Grpc.Core;
using ProtocolClient = Asterloom.Protocol.Identity.V1.OidcClient;
using ProtocolClientCredential = Asterloom.Protocol.Identity.V1.OidcClientCredential;
using ProtocolScope = Asterloom.Protocol.Identity.V1.OidcScope;
using ProtocolSession = Asterloom.Protocol.Identity.V1.IdentitySession;
using ProtocolUser = Asterloom.Protocol.Identity.V1.IdentityUser;
using ProtocolUserInvitation = Asterloom.Protocol.Identity.V1.UserInvitation;
using ProtocolMembership = Asterloom.Protocol.Identity.V1.ApplicationMembership;

namespace Asterloom.Modules.Identity.Management;

internal sealed class IdentityAdminGrpcService(
    IdentityManagementService managementService)
    : IdentityAdminService.IdentityAdminServiceBase
{
    public override async Task<ListUsersResponse> ListUsers(
        ListUsersRequest request,
        ServerCallContext context)
    {
        var page = await managementService.ListUsersAsync(
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListUsersResponse { NextPageToken = page.NextPageToken };
        response.Users.AddRange(page.Items.Select(IdentityProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolUser> GetUser(
        GetUserRequest request,
        ServerCallContext context) =>
        (await managementService.GetUserAsync(
            request.UserId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUser> CreateUser(
        CreateUserRequest request,
        ServerCallContext context) =>
        (await managementService.CreateUserAsync(
            request.Email,
            request.DisplayName,
            request.Password,
            request.EmailConfirmed,
            request.Roles,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUserInvitation> InviteUser(
        InviteUserRequest request,
        ServerCallContext context) =>
        (await managementService.InviteUserAsync(
            request.Email,
            request.DisplayName,
            request.Roles,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUserInvitation> ResendUserInvitation(
        ResendUserInvitationRequest request,
        ServerCallContext context) =>
        (await managementService.ResendInvitationAsync(
            request.UserId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUser> UpdateUser(
        UpdateUserRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateUserAsync(
            request.UserId,
            request.DisplayName,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUser> SetUserRoles(
        SetUserRolesRequest request,
        ServerCallContext context) =>
        (await managementService.SetUserRolesAsync(
            request.UserId,
            request.Roles,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUser> ResetUserPassword(
        ResetUserPasswordRequest request,
        ServerCallContext context) =>
        (await managementService.ResetUserPasswordAsync(
            request.UserId,
            request.NewPassword,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUser> SuspendUser(
        UserVersionRequest request,
        ServerCallContext context) =>
        (await managementService.SuspendUserAsync(
            request.UserId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUser> ReactivateUser(
        UserVersionRequest request,
        ServerCallContext context) =>
        (await managementService.ReactivateUserAsync(
            request.UserId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUser> ArchiveUser(
        UserVersionRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveUserAsync(
            request.UserId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUser> RestoreUser(
        UserVersionRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreUserAsync(
            request.UserId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListUserSessionsResponse> ListUserSessions(
        ListUserSessionsRequest request,
        ServerCallContext context)
    {
        var page = await managementService.ListUserSessionsAsync(
            request.UserId,
            request.PageSize,
            request.PageToken,
            request.IncludeRevoked,
            context.CancellationToken);
        var response = new ListUserSessionsResponse { NextPageToken = page.NextPageToken };
        response.Sessions.AddRange(page.Items.Select(IdentityProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolSession> RevokeUserSession(
        RevokeUserSessionRequest request,
        ServerCallContext context) =>
        (await managementService.RevokeUserSessionAsync(
            request.UserId,
            request.SessionId,
            context.CancellationToken)).ToProtocol();

    public override async Task<RevokeAllUserSessionsResponse> RevokeAllUserSessions(
        RevokeAllUserSessionsRequest request,
        ServerCallContext context) =>
        new()
        {
            RevokedSessions = await managementService.RevokeAllUserSessionsAsync(
                request.UserId,
                context.CancellationToken),
        };

    public override async Task<ListApplicationMembershipsResponse> ListApplicationMemberships(
        ListApplicationMembershipsRequest request,
        ServerCallContext context)
    {
        var page = await managementService.ListApplicationMembershipsAsync(
            request.PageSize,
            request.PageToken,
            request.UserId,
            request.TenantId,
            request.ApplicationId,
            request.IncludeRemoved,
            context.CancellationToken);
        var response = new ListApplicationMembershipsResponse
        {
            NextPageToken = page.NextPageToken,
        };
        response.Memberships.AddRange(page.Items.Select(IdentityProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolMembership> SetApplicationMembership(
        SetApplicationMembershipRequest request,
        ServerCallContext context) =>
        (await managementService.SetApplicationMembershipAsync(
            request.UserId,
            request.TenantId,
            request.ApplicationId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolMembership> RemoveApplicationMembership(
        RemoveApplicationMembershipRequest request,
        ServerCallContext context) =>
        (await managementService.RemoveApplicationMembershipAsync(
            request.UserId,
            request.ApplicationId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListClientsResponse> ListClients(
        ListClientsRequest request,
        ServerCallContext context)
    {
        var page = await managementService.ListClientsAsync(
            request.PageSize,
            request.PageToken,
            request.Query,
            context.CancellationToken);
        var response = new ListClientsResponse { NextPageToken = page.NextPageToken };
        response.Clients.AddRange(page.Items.Select(IdentityProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolClient> GetClient(
        GetClientRequest request,
        ServerCallContext context) =>
        (await managementService.GetClientAsync(
            request.ClientId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolClientCredential> CreateClient(
        CreateClientRequest request,
        ServerCallContext context) =>
        (await managementService.CreateClientAsync(
            request.ClientId,
            request.DisplayName,
            request.ClientType.ToDomain(),
            request.ApplicationType.ToDomain(),
            request.GrantTypes.Select(IdentityProtocolMapper.ToDomain),
            request.RedirectUris,
            request.PostLogoutRedirectUris,
            request.Scopes,
            request.TenantId,
            request.ApplicationId,
            request.AllowUserRegistration,
            request.AllowMembershipAutoJoin,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolClient> UpdateClient(
        UpdateClientRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateClientAsync(
            request.ClientId,
            request.DisplayName,
            request.GrantTypes.Select(IdentityProtocolMapper.ToDomain),
            request.RedirectUris,
            request.PostLogoutRedirectUris,
            request.Scopes,
            request.TenantId,
            request.ApplicationId,
            request.AllowUserRegistration,
            request.AllowMembershipAutoJoin,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolClientCredential> RotateClientSecret(
        RotateClientSecretRequest request,
        ServerCallContext context) =>
        (await managementService.RotateClientSecretAsync(
            request.ClientId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolClient> DeleteClient(
        DeleteClientRequest request,
        ServerCallContext context) =>
        (await managementService.DeleteClientAsync(
            request.ClientId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListScopesResponse> ListScopes(
        ListScopesRequest request,
        ServerCallContext context)
    {
        var page = await managementService.ListScopesAsync(
            request.PageSize,
            request.PageToken,
            request.Query,
            context.CancellationToken);
        var response = new ListScopesResponse { NextPageToken = page.NextPageToken };
        response.Scopes.AddRange(page.Items.Select(IdentityProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolScope> GetScope(
        GetScopeRequest request,
        ServerCallContext context) =>
        (await managementService.GetScopeAsync(
            request.ScopeId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolScope> CreateScope(
        CreateScopeRequest request,
        ServerCallContext context) =>
        (await managementService.CreateScopeAsync(
            request.Name,
            request.DisplayName,
            request.Description,
            request.Resources,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolScope> UpdateScope(
        UpdateScopeRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateScopeAsync(
            request.ScopeId,
            request.DisplayName,
            request.Description,
            request.Resources,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolScope> DeleteScope(
        DeleteScopeRequest request,
        ServerCallContext context) =>
        (await managementService.DeleteScopeAsync(
            request.ScopeId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();
}
