using System.Security.Claims;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Identity;
using Asterloom.Protocol.Authorization.V1;
using Grpc.Core;

namespace Asterloom.Modules.Authorization;

internal sealed class AuthorizationGrpcService(
    AuthorizationManagementService managementService,
    IApplicationMembershipValidator memberships)
    : AuthorizationService.AuthorizationServiceBase
{
    public override async Task<AuthorizationDecision> CheckPermission(
        AuthorizationDecisionInput request,
        ServerCallContext context)
    {
        var principal = context.GetHttpContext().User;
        var actorId = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new AsterloomException(
                AsterloomErrorKind.Unauthenticated,
                "actor_identity_missing",
                "The access token has no stable subject.");
        var callerType = principal.FindFirstValue("asterloom_actor_type");
        var isServiceClient = string.Equals(
            callerType,
            "client",
            StringComparison.Ordinal);
        var targetActorId = string.IsNullOrWhiteSpace(request.ActorId)
            ? actorId
            : request.ActorId;
        if (!isServiceClient
            && !string.Equals(targetActorId, actorId, StringComparison.Ordinal))
        {
            throw new AsterloomException(
                AsterloomErrorKind.PermissionDenied,
                "actor_impersonation_denied",
                "A caller may only check its own permissions.");
        }

        if (request.Attributes.Count > 0 && !isServiceClient)
        {
            throw new AsterloomException(
                AsterloomErrorKind.PermissionDenied,
                "authorization_trusted_attributes_required",
                "Only a confidential application service may supply ABAC attributes.");
        }

        var trustedRoles = principal.Claims
            .Where(claim => claim.Type is "role" || claim.Type == ClaimTypes.Role)
            .Select(static claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var scope = ApplicationTokenScope.Enforce(
            principal,
            request.Scope.ToDomain(),
            inferWhenUnspecified: true);
        if (request.Attributes.Count > 0 && scope.ApplicationId is null)
        {
            throw new AsterloomException(
                AsterloomErrorKind.PermissionDenied,
                "authorization_application_scope_required",
                "ABAC attributes require an application-bound confidential client.");
        }
        await ApplicationTokenScope.EnforceMembershipAsync(
            principal,
            memberships,
            context.CancellationToken);
        if (isServiceClient
            && !string.Equals(targetActorId, actorId, StringComparison.Ordinal))
        {
            if (scope.ApplicationId is not { } applicationId
                || !Guid.TryParse(targetActorId, out var userId)
                || userId == Guid.Empty
                || !await memberships.IsActiveMemberAsync(
                    userId,
                    applicationId,
                    context.CancellationToken))
            {
                throw new AsterloomException(
                    AsterloomErrorKind.PermissionDenied,
                    "identity_application_membership_required",
                    "The target account is not an active member of the application.");
            }
        }

        var input = request.ToDomain() with
        {
            ActorId = targetActorId,
            Scope = scope,
            TrustedRoles = string.Equals(targetActorId, actorId, StringComparison.Ordinal)
                ? trustedRoles
                : [],
        };
        var decision = await managementService.SimulateAsync(
            input,
            context.CancellationToken);
        return decision.ToProtocol();
    }
}
