namespace Asterloom.Sdk.Identity.AspNetCore;

public sealed class AsterloomResourceServerOptions
{
    public Uri? Issuer { get; set; }

    public Uri? AuthorizationServer { get; set; }

    public string Audience { get; set; } = string.Empty;

    public Guid? TenantId { get; set; }

    public Guid? ApplicationId { get; set; }

    public bool AllowInsecureHttpForDevelopment { get; set; }
}
