using Microsoft.Extensions.Configuration;

namespace Asterloom.Modules.Identity.Bootstrap;

public sealed record IdentityBootstrapOptions(
    string? AdminEmail,
    string? AdminPassword,
    string AdminDisplayName,
    string? WebClientId,
    string? WebClientSecret,
    Uri? WebClientRedirectUri,
    Uri? WebClientPostLogoutRedirectUri)
{
    public static IdentityBootstrapOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var adminEmail = Normalize(configuration["Identity:Bootstrap:AdminEmail"]);
        var adminPassword = Normalize(configuration["Identity:Bootstrap:AdminPassword"]);
        if ((adminEmail is null) != (adminPassword is null))
        {
            throw new InvalidOperationException(
                "Identity bootstrap requires both AdminEmail and AdminPassword, or neither.");
        }

        return new IdentityBootstrapOptions(
            adminEmail,
            adminPassword,
            Normalize(configuration["Identity:Bootstrap:AdminDisplayName"])
                ?? "Asterloom Administrator",
            Normalize(configuration["Identity:WebClient:ClientId"]),
            Normalize(configuration["Identity:WebClient:ClientSecret"]),
            ParseOptionalUri(
                configuration["Identity:WebClient:RedirectUri"],
                "Identity:WebClient:RedirectUri"),
            ParseOptionalUri(
                configuration["Identity:WebClient:PostLogoutRedirectUri"],
                "Identity:WebClient:PostLogoutRedirectUri"));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Uri? ParseOptionalUri(string? value, string key)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{key} must be an absolute URI without a fragment.");
        }

        return uri;
    }
}
