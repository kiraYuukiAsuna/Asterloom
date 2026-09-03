using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Client;
using OpenIddict.EntityFrameworkCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.Sdk.Identity;

public static class AsterloomIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddAsterloomIdentityClient(
        this IServiceCollection services,
        Action<AsterloomIdentityClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var configuration = new AsterloomIdentityClientOptions();
        configure(configuration);
        Validate(configuration);
        services.AddSingleton(configuration);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IAsterloomTokenStore, AsterloomInMemoryTokenStore>();
        services.TryAddSingleton<IAsterloomIdentityProtocolClient>(provider =>
            new OpenIddictIdentityProtocolClient(
                provider.GetRequiredService<OpenIddictClientService>()));
        services.TryAddSingleton(provider => new AsterloomIdentityClient(
            provider.GetRequiredService<IAsterloomIdentityProtocolClient>(),
            provider.GetRequiredService<AsterloomIdentityClientOptions>(),
            provider.GetRequiredService<IAsterloomTokenStore>(),
            provider.GetRequiredService<TimeProvider>()));

        var openIddict = services.AddOpenIddict();
        if (configuration.EnableInteractiveAuthentication)
        {
            var databaseName = $"Asterloom.Identity.Client.{configuration.RegistrationId}";
            services.AddDbContext<AsterloomIdentityClientDbContext>(options =>
            {
                options.UseInMemoryDatabase(databaseName);
                options.UseOpenIddict();
            });
            openIddict.AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<AsterloomIdentityClientDbContext>());
        }

        openIddict.AddClient(options =>
        {
            if (configuration.EnableInteractiveAuthentication)
            {
                options.AllowAuthorizationCodeFlow();
                if (configuration.RequestRefreshTokens)
                {
                    options.AllowRefreshTokenFlow();
                }

                options.AddEphemeralEncryptionKey()
                    .AddEphemeralSigningKey();
                var integration = options.UseSystemIntegration()
                    .EnableEmbeddedWebServer();
                if (configuration.AllowedEmbeddedWebServerPorts.Count > 0)
                {
                    integration.SetAllowedEmbeddedWebServerPorts(
                        configuration.AllowedEmbeddedWebServerPorts.ToArray());
                }
            }

            if (configuration.EnableServiceCredentials)
            {
                options.AllowClientCredentialsFlow();
                options.DisableTokenStorage();
            }

            options.UseSystemNetHttp()
                .SetProductInformation(typeof(AsterloomIdentityClient).Assembly);

            var registration = new OpenIddictClientRegistration
            {
                RegistrationId = configuration.RegistrationId,
                ProviderName = "Asterloom",
                Issuer = configuration.Issuer,
                ClientId = configuration.ClientId,
                ClientSecret = configuration.ClientSecret,
                RedirectUri = configuration.EnableInteractiveAuthentication
                    ? configuration.RedirectUri
                    : null,
                PostLogoutRedirectUri = configuration.EnableInteractiveAuthentication
                    ? configuration.PostLogoutRedirectUri
                    : null,
            };
            registration.Scopes.UnionWith(configuration.Scopes);
            if (configuration.EnableInteractiveAuthentication)
            {
                registration.Scopes.Add(Scopes.OpenId);
                registration.Scopes.Add(Scopes.Profile);
                registration.Scopes.Add(Scopes.Email);
                registration.Scopes.Add("roles");
                if (configuration.RequestRefreshTokens)
                {
                    registration.Scopes.Add(Scopes.OfflineAccess);
                }
            }
            options.AddRegistration(registration);
        });

        return services;
    }

    private static void Validate(AsterloomIdentityClientOptions options)
    {
        if (options.Issuer is null || !options.Issuer.IsAbsoluteUri)
        {
            throw new ArgumentException("An absolute Passport issuer URI is required.", nameof(options));
        }

        ValidateIssuer(options.Issuer, options.AllowInsecureHttpForDevelopment);
        RequireText(options.ClientId, nameof(options.ClientId), 100);
        RequireText(options.RegistrationId, nameof(options.RegistrationId), 100);
        if (!options.EnableInteractiveAuthentication
            && !options.EnableServiceCredentials)
        {
            throw new ArgumentException(
                "Enable interactive or service-credential authentication.",
                nameof(options));
        }

        if (options.EnableInteractiveAuthentication
            && options.EnableServiceCredentials)
        {
            throw new ArgumentException(
                "Use separate public and confidential registrations for interactive and service authentication.",
                nameof(options));
        }

        if (options.EnableInteractiveAuthentication)
        {
            if (!string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                throw new ArgumentException(
                    "Native interactive clients must be public and cannot embed a client secret.",
                    nameof(options));
            }

            ValidateRedirectUri(options.RedirectUri, nameof(options.RedirectUri));
            ValidateRedirectUri(
                options.PostLogoutRedirectUri,
                nameof(options.PostLogoutRedirectUri));
            if (options.RedirectUri == options.PostLogoutRedirectUri)
            {
                throw new ArgumentException(
                    "RedirectUri and PostLogoutRedirectUri must be different.",
                    nameof(options));
            }
        }

        if (options.EnableServiceCredentials)
        {
            RequireText(options.ClientSecret, nameof(options.ClientSecret), 2_048);
        }

        if (options.RefreshBeforeExpiration < TimeSpan.Zero
            || options.RefreshBeforeExpiration > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.RefreshBeforeExpiration,
                "RefreshBeforeExpiration must be between zero and ten minutes.");
        }

        if (options.Scopes.Count == 0
            || options.Scopes.Any(static scope => string.IsNullOrWhiteSpace(scope)))
        {
            throw new ArgumentException("At least one valid scope is required.", nameof(options));
        }

        if (options.AllowedEmbeddedWebServerPorts.Any(static port => port is < 1 or > 65_535)
            || options.AllowedEmbeddedWebServerPorts.Count
                != options.AllowedEmbeddedWebServerPorts.Distinct().Count())
        {
            throw new ArgumentException(
                "Embedded Web server ports must be unique values between 1 and 65535.",
                nameof(options));
        }
    }

    private static void ValidateIssuer(Uri issuer, bool allowInsecure)
    {
        if (!string.IsNullOrEmpty(issuer.Query)
            || !string.IsNullOrEmpty(issuer.Fragment)
            || !string.IsNullOrEmpty(issuer.UserInfo))
        {
            throw new ArgumentException(
                "The Passport issuer cannot contain user information, a query, or a fragment.",
                nameof(issuer));
        }

        if (string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!allowInsecure
            || !string.Equals(issuer.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || !issuer.IsLoopback)
        {
            throw new ArgumentException(
                "Passport issuers must use HTTPS. Plain HTTP is limited to explicitly enabled loopback development issuers.",
                nameof(issuer));
        }
    }

    private static void ValidateRedirectUri(Uri value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.IsAbsoluteUri || !string.IsNullOrEmpty(value.Fragment))
        {
            throw new ArgumentException(
                "Redirect URIs must be absolute and cannot contain a fragment.",
                parameterName);
        }

        if (string.Equals(value.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !value.IsLoopback)
        {
            throw new ArgumentException(
                "Plain HTTP redirects are limited to loopback addresses.",
                parameterName);
        }
    }

    private static string RequireText(string? value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A value between 1 and {maximumLength} characters is required.",
                parameterName);
        }

        return normalized;
    }
}

internal sealed class AsterloomIdentityClientDbContext(
    DbContextOptions<AsterloomIdentityClientDbContext> options) : DbContext(options);
