using Asterloom.Modules.Errors;
using Google.Protobuf.WellKnownTypes;
using ProtocolClient = Asterloom.Protocol.Identity.V1.OidcClient;
using ProtocolClientCredential = Asterloom.Protocol.Identity.V1.OidcClientCredential;
using ProtocolApplicationType = Asterloom.Protocol.Identity.V1.OidcApplicationType;
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

namespace Asterloom.Modules.Identity.Management;

internal static class IdentityProtocolMapper
{
    public static ProtocolUser ToProtocol(this ManagedIdentityUser user)
    {
        var result = new ProtocolUser
        {
            Id = user.Id.ToString("D"),
            Email = user.Email,
            DisplayName = user.DisplayName,
            Status = user.Status switch
            {
                Model.AsterloomUserStatus.Pending => ProtocolUserStatus.Pending,
                Model.AsterloomUserStatus.Active => ProtocolUserStatus.Active,
                Model.AsterloomUserStatus.Suspended => ProtocolUserStatus.Suspended,
                Model.AsterloomUserStatus.Archived => ProtocolUserStatus.Archived,
                _ => ProtocolUserStatus.Unspecified,
            },
            Version = user.Version,
            CreatedAt = user.CreatedAt.ToTimestamp(),
            UpdatedAt = user.UpdatedAt.ToTimestamp(),
            EmailConfirmed = user.EmailConfirmed,
        };
        result.Roles.AddRange(user.Roles);
        if (user.ArchivedAt is not null)
        {
            result.ArchivedAt = user.ArchivedAt.Value.ToTimestamp();
        }

        return result;
    }

    public static ProtocolMembership ToProtocol(
        this ManagedApplicationMembership membership) =>
        new()
        {
            UserId = membership.UserId.ToString("D"),
            TenantId = membership.TenantId.ToString("D"),
            ApplicationId = membership.ApplicationId.ToString("D"),
            Status = membership.Status switch
            {
                Model.AsterloomApplicationMembershipStatus.Active =>
                    ProtocolMembershipStatus.Active,
                Model.AsterloomApplicationMembershipStatus.Removed =>
                    ProtocolMembershipStatus.Removed,
                _ => ProtocolMembershipStatus.Unspecified,
            },
            Version = membership.Version,
            CreatedAt = membership.CreatedAt.ToTimestamp(),
            UpdatedAt = membership.UpdatedAt.ToTimestamp(),
        };

    public static ProtocolUserInvitation ToProtocol(this ManagedUserInvitation invitation) =>
        new()
        {
            User = invitation.User.ToProtocol(),
            InvitationUrl = invitation.InvitationUrl,
            ExpiresAt = invitation.ExpiresAt.ToTimestamp(),
        };

    public static ProtocolClient ToProtocol(this ManagedOidcClient client)
    {
        var result = new ProtocolClient
        {
            Id = client.Id,
            ClientId = client.ClientId,
            DisplayName = client.DisplayName,
            ClientType = client.ClientType switch
            {
                ManagedOidcClientType.Public => ProtocolClientType.Public,
                ManagedOidcClientType.Confidential => ProtocolClientType.Confidential,
                _ => ProtocolClientType.Unspecified,
            },
            ApplicationType = client.ApplicationType switch
            {
                ManagedOidcApplicationType.Web => ProtocolApplicationType.Web,
                ManagedOidcApplicationType.Native => ProtocolApplicationType.Native,
                _ => ProtocolApplicationType.Unspecified,
            },
            Version = client.Version,
            TenantId = client.TenantId?.ToString("D") ?? string.Empty,
            ApplicationId = client.ApplicationId?.ToString("D") ?? string.Empty,
            AllowUserRegistration = client.AllowUserRegistration,
            AllowMembershipAutoJoin = client.AllowMembershipAutoJoin,
            IsSystem = client.IsSystem,
            IsMutable = client.IsMutable,
        };
        result.GrantTypes.AddRange(client.GrantTypes.Select(ToProtocol));
        result.RedirectUris.AddRange(client.RedirectUris);
        result.PostLogoutRedirectUris.AddRange(client.PostLogoutRedirectUris);
        result.Scopes.AddRange(client.Scopes);
        return result;
    }

    public static ProtocolClientCredential ToProtocol(
        this ManagedOidcClientCredential credential) =>
        new()
        {
            Client = credential.Client.ToProtocol(),
            ClientSecret = credential.ClientSecret,
        };

    public static ProtocolScope ToProtocol(this ManagedOidcScope scope)
    {
        var result = new ProtocolScope
        {
            Id = scope.Id,
            Name = scope.Name,
            DisplayName = scope.DisplayName,
            Description = scope.Description,
            Version = scope.Version,
            IsSystem = scope.IsSystem,
            IsMutable = scope.IsMutable,
        };
        result.Resources.AddRange(scope.Resources);
        return result;
    }

    public static ProtocolSession ToProtocol(this ManagedIdentitySession session)
    {
        var result = new ProtocolSession
        {
            Id = session.Id,
            UserId = session.UserId.ToString("D"),
            ClientId = session.ClientId,
            ClientDisplayName = session.ClientDisplayName,
            Status = session.IsRevoked
                ? ProtocolSessionStatus.Revoked
                : ProtocolSessionStatus.Valid,
            CreatedAt = session.CreatedAt.ToTimestamp(),
        };
        result.Scopes.AddRange(session.Scopes);
        return result;
    }

    public static ManagedOidcClientType ToDomain(this ProtocolClientType type) => type switch
    {
        ProtocolClientType.Public => ManagedOidcClientType.Public,
        ProtocolClientType.Confidential => ManagedOidcClientType.Confidential,
        _ => throw Invalid("clientType", "A supported OIDC client type is required."),
    };

    public static ManagedOidcApplicationType ToDomain(this ProtocolApplicationType type) =>
        type switch
        {
            ProtocolApplicationType.Web => ManagedOidcApplicationType.Web,
            ProtocolApplicationType.Native => ManagedOidcApplicationType.Native,
            _ => throw Invalid(
                "applicationType",
                "A supported OIDC application type is required."),
        };

    public static ManagedOidcGrantType ToDomain(this ProtocolGrantType type) => type switch
    {
        ProtocolGrantType.AuthorizationCode => ManagedOidcGrantType.AuthorizationCode,
        ProtocolGrantType.ClientCredentials => ManagedOidcGrantType.ClientCredentials,
        ProtocolGrantType.RefreshToken => ManagedOidcGrantType.RefreshToken,
        _ => throw Invalid("grantTypes", "A supported OIDC grant type is required."),
    };

    private static ProtocolGrantType ToProtocol(ManagedOidcGrantType type) => type switch
    {
        ManagedOidcGrantType.AuthorizationCode => ProtocolGrantType.AuthorizationCode,
        ManagedOidcGrantType.ClientCredentials => ProtocolGrantType.ClientCredentials,
        ManagedOidcGrantType.RefreshToken => ProtocolGrantType.RefreshToken,
        _ => ProtocolGrantType.Unspecified,
    };

    private static AsterloomException Invalid(string field, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "validation_failed",
        "One or more fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        });
}
