namespace Asterloom.Sdk.Identity.AspNetCore;

public static class AsterloomResourceServerDefaults
{
    public const string AuthenticationScheme = "Asterloom";

    public const string AuthorizationHttpClientName =
        "Asterloom.ResourceServer.Authorization";

    public const string SubjectClaim = "sub";

    public const string ActorTypeClaim = "asterloom_actor_type";

    public const string UserActor = "user";

    public const string TenantIdClaim = "tenant_id";

    public const string ApplicationIdClaim = "application_id";
}
