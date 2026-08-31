namespace Asterloom.ReferenceApp.Backend;

internal sealed record ReferenceIdentityOptions(
    bool Enabled,
    Uri AsterloomBaseAddress,
    Uri PassportIssuer,
    string ClientId,
    string ClientSecret,
    bool AllowInsecureHttpForDevelopment,
    bool ExposeEmailVerificationToken)
{
    public static ReferenceIdentityOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Asterloom:Identity");
        var enabled = section.GetValue<bool>("Enabled");
        var baseAddress = ReadUri(
            section["BaseAddress"],
            "http://localhost:5080/",
            "Asterloom:Identity:BaseAddress");
        var issuer = ReadUri(
            section["Issuer"],
            baseAddress.AbsoluteUri,
            "Asterloom:Identity:Issuer");
        var clientId = section["ClientId"]?.Trim() ?? string.Empty;
        var clientSecret = section["ClientSecret"] ?? string.Empty;
        if (enabled && (clientId.Length == 0 || clientSecret.Length == 0))
        {
            throw new InvalidOperationException(
                "Asterloom:Identity:ClientId and ClientSecret are required when the reference Identity BFF is enabled.");
        }

        return new ReferenceIdentityOptions(
            enabled,
            baseAddress,
            issuer,
            clientId,
            clientSecret,
            section.GetValue<bool>("AllowInsecureHttpForDevelopment"),
            section.GetValue<bool>("ExposeEmailVerificationToken"));
    }

    private static Uri ReadUri(string? value, string fallback, string name) =>
        Uri.TryCreate(
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim(),
            UriKind.Absolute,
            out var uri)
            ? uri
            : throw new InvalidOperationException($"{name} must be an absolute URI.");
}
