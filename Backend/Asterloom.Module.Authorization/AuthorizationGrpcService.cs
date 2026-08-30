using System.Security.Claims;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Errors;
using Asterloom.Protocol.Authorization.V1;
using Grpc.Core;

namespace Asterloom.Modules.Authorization;

internal sealed class AuthorizationGrpcService(
    AuthorizationManagementService managementService)
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
        if (!string.IsNullOrWhiteSpace(request.ActorId)
            && !string.Equals(request.ActorId, actorId, StringComparison.Ordinal))
        {
            throw new AsterloomException(
                AsterloomErrorKind.PermissionDenied,
                "actor_impersonation_denied",
                "A caller may only check its own permissions.");
        }

        var trustedRoles = principal.Claims
            .Where(claim => claim.Type is "role" || claim.Type == ClaimTypes.Role)
            .Select(static claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var decision = await managementService.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                request.Scope.ToDomain(),
                request.Permission,
                trustedRoles),
            context.CancellationToken);
        return decision.ToProtocol();
    }
}
