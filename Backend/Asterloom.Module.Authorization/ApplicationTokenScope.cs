using System.Security.Claims;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Identity;

namespace Asterloom.Modules.Authorization;

internal static class ApplicationTokenScope
{
    private const string TenantIdClaim = "tenant_id";
    private const string ApplicationIdClaim = "application_id";

    public static AuthorizationScope Enforce(
        ClaimsPrincipal principal,
        AuthorizationScope requestedScope,
        bool inferWhenUnspecified)
    {
        var tenantClaim = principal.FindFirstValue(TenantIdClaim);
        var applicationClaim = principal.FindFirstValue(ApplicationIdClaim);
        if (string.IsNullOrWhiteSpace(tenantClaim)
            && string.IsNullOrWhiteSpace(applicationClaim))
        {
            return requestedScope;
        }

        if (!Guid.TryParse(tenantClaim, out var tenantId)
            || tenantId == Guid.Empty
            || !Guid.TryParse(applicationClaim, out var applicationId)
            || applicationId == Guid.Empty)
        {
            throw Denied("token_application_scope_invalid");
        }

        if (requestedScope.TenantId is null && requestedScope.ApplicationId is null)
        {
            if (inferWhenUnspecified)
            {
                return new AuthorizationScope(tenantId, applicationId, null);
            }

            throw Denied("token_application_scope_required");
        }

        if (requestedScope.TenantId != tenantId
            || requestedScope.ApplicationId != applicationId)
        {
            throw Denied("token_application_scope_mismatch");
        }

        return requestedScope;
    }

    public static async Task EnforceMembershipAsync(
        ClaimsPrincipal principal,
        IApplicationMembershipValidator memberships,
        CancellationToken cancellationToken)
    {
        var applicationClaim = principal.FindFirstValue(ApplicationIdClaim);
        if (string.IsNullOrWhiteSpace(applicationClaim)
            || !string.Equals(
                principal.FindFirstValue("asterloom_actor_type"),
                "user",
                StringComparison.Ordinal))
        {
            return;
        }

        var subject = principal.FindFirstValue("sub");
        if (!Guid.TryParse(subject, out var userId)
            || userId == Guid.Empty
            || !Guid.TryParse(applicationClaim, out var applicationId)
            || applicationId == Guid.Empty
            || !await memberships.IsActiveMemberAsync(
                userId,
                applicationId,
                cancellationToken))
        {
            throw new AsterloomException(
                AsterloomErrorKind.PermissionDenied,
                "identity_application_membership_required",
                "The account is not an active member of the application bound to the access token.");
        }
    }

    private static AsterloomException Denied(string errorCode) => new(
        AsterloomErrorKind.PermissionDenied,
        errorCode,
        "The requested scope is outside the application bound to the access token.");
}
