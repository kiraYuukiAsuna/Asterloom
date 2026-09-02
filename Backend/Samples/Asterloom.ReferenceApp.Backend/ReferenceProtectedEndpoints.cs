using System.Security.Claims;

namespace Asterloom.ReferenceApp.Backend;

internal static class ReferenceProtectedEndpoints
{
    public const string PlatformReadPolicy = "reference-platform-read";

    public static IEndpointRouteBuilder MapReferenceProtectedEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/reference/me",
                (ClaimsPrincipal user) => Results.Ok(new
                {
                    authenticated = true,
                    subject = user.FindFirstValue("sub"),
                    name = user.FindFirstValue("name"),
                    email = user.FindFirstValue("email"),
                    tenantId = user.FindFirstValue("tenant_id"),
                    applicationId = user.FindFirstValue("application_id"),
                    actorType = user.FindFirstValue("asterloom_actor_type"),
                }))
            .RequireAuthorization();
        endpoints.MapGet(
                "/api/reference/platform-access",
                () => Results.Ok(new
                {
                    allowed = true,
                    permission = "platform.info.read",
                }))
            .RequireAuthorization(PlatformReadPolicy);
        return endpoints;
    }
}
