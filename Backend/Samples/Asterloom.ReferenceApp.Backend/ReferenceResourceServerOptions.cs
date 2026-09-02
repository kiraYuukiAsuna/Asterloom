namespace Asterloom.ReferenceApp.Backend;

internal sealed record ReferenceResourceServerOptions(
    bool Enabled,
    Uri Issuer,
    Uri AuthorizationServer,
    string Audience,
    Guid TenantId,
    Guid ApplicationId,
    bool AllowInsecureHttpForDevelopment)
{
    public static ReferenceResourceServerOptions FromConfiguration(
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Asterloom:ResourceServer");
        var enabled = section.GetValue<bool>("Enabled");
        var issuer = ReadUri(
            section["Issuer"],
            "http://localhost:5080/",
            "Asterloom:ResourceServer:Issuer");
        var authorizationServer = ReadUri(
            section["AuthorizationServer"],
            issuer.AbsoluteUri,
            "Asterloom:ResourceServer:AuthorizationServer");
        var audience = section["Audience"]?.Trim() ?? string.Empty;
        var tenantId = ReadId(section["TenantId"]);
        var applicationId = ReadId(section["ApplicationId"]);
        if (enabled
            && (audience.Length == 0
                || tenantId == Guid.Empty
                || applicationId == Guid.Empty))
        {
            throw new InvalidOperationException(
                "Audience, TenantId, and ApplicationId are required when the reference resource server is enabled.");
        }

        return new ReferenceResourceServerOptions(
            enabled,
            issuer,
            authorizationServer,
            audience,
            tenantId,
            applicationId,
            section.GetValue<bool>("AllowInsecureHttpForDevelopment"));
    }

    private static Guid ReadId(string? value) =>
        Guid.TryParse(value, out var id) ? id : Guid.Empty;

    private static Uri ReadUri(string? value, string fallback, string name) =>
        Uri.TryCreate(
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim(),
            UriKind.Absolute,
            out var uri)
            ? uri
            : throw new InvalidOperationException($"{name} must be an absolute URI.");
}
