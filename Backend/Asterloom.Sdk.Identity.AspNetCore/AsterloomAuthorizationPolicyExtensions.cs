using Microsoft.AspNetCore.Authorization;

namespace Asterloom.Sdk.Identity.AspNetCore;

public static class AsterloomAuthorizationPolicyExtensions
{
    public static AuthorizationPolicyBuilder RequireAsterloomPermission(
        this AuthorizationPolicyBuilder policy,
        string permission,
        Guid? environmentId = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var normalized = permission?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 200)
        {
            throw new ArgumentException(
                "A permission key between 1 and 200 characters is required.",
                nameof(permission));
        }

        return policy.AddRequirements(
            new AsterloomPermissionRequirement(normalized, environmentId));
    }
}
