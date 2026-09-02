using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Asterloom.Sdk.Identity.AspNetCore;

public static class AsterloomResourceServerServiceCollectionExtensions
{
    public static IServiceCollection AddAsterloomResourceServer(
        this IServiceCollection services,
        Action<AsterloomResourceServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AsterloomResourceServerOptions();
        configure(options);
        Validate(options);

        var issuer = options.Issuer!;
        var authorizationServer = options.AuthorizationServer ?? issuer;
        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services
            .AddAuthentication(AsterloomResourceServerDefaults.AuthenticationScheme)
            .AddJwtBearer(
                AsterloomResourceServerDefaults.AuthenticationScheme,
                jwt =>
                {
                    jwt.Authority = issuer.AbsoluteUri;
                    jwt.Audience = options.Audience;
                    jwt.MapInboundClaims = false;
                    jwt.RequireHttpsMetadata = !options.AllowInsecureHttpForDevelopment;
                    jwt.SaveToken = true;
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = issuer.AbsoluteUri,
                        ValidateAudience = true,
                        ValidAudience = options.Audience,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                        ValidTypes = ["at+jwt"],
                        NameClaimType = "name",
                        RoleClaimType = "role",
                    };
                    jwt.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context =>
                        {
                            ValidateClaims(context, options);
                            return Task.CompletedTask;
                        },
                    };
                });
        services.AddAuthorization();
        services.AddHttpClient(
            AsterloomResourceServerDefaults.AuthorizationHttpClientName,
            client => client.BaseAddress = EnsureTrailingSlash(authorizationServer));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAuthorizationHandler,
                AsterloomPermissionAuthorizationHandler>());
        return services;
    }

    private static void ValidateClaims(
        TokenValidatedContext context,
        AsterloomResourceServerOptions options)
    {
        var principal = context.Principal;
        if (principal is null
            || string.IsNullOrWhiteSpace(
                principal.FindFirstValue(AsterloomResourceServerDefaults.SubjectClaim)))
        {
            context.Fail("The access token has no stable subject.");
            return;
        }

        if (!string.Equals(
            principal.FindFirstValue(AsterloomResourceServerDefaults.ActorTypeClaim),
            AsterloomResourceServerDefaults.UserActor,
            StringComparison.Ordinal))
        {
            context.Fail("The access token does not represent an Asterloom user.");
            return;
        }

        if (!MatchesExpectedId(
            principal,
            AsterloomResourceServerDefaults.TenantIdClaim,
            options.TenantId))
        {
            context.Fail("The access token belongs to a different tenant.");
            return;
        }

        if (!MatchesExpectedId(
            principal,
            AsterloomResourceServerDefaults.ApplicationIdClaim,
            options.ApplicationId))
        {
            context.Fail("The access token belongs to a different application.");
        }
    }

    private static bool MatchesExpectedId(
        ClaimsPrincipal principal,
        string claimType,
        Guid? expected)
    {
        if (expected is null)
        {
            return true;
        }

        return Guid.TryParse(principal.FindFirstValue(claimType), out var actual)
            && actual == expected.Value;
    }

    private static void Validate(AsterloomResourceServerOptions options)
    {
        ValidateEndpoint(
            options.Issuer,
            options.AllowInsecureHttpForDevelopment,
            nameof(options.Issuer));
        if (options.AuthorizationServer is not null)
        {
            ValidateEndpoint(
                options.AuthorizationServer,
                options.AllowInsecureHttpForDevelopment,
                nameof(options.AuthorizationServer));
        }

        options.Audience = options.Audience.Trim();
        if (options.Audience.Length is 0 or > 200)
        {
            throw new ArgumentException(
                "A resource audience between 1 and 200 characters is required.",
                nameof(options));
        }

        if (options.ApplicationId is not null && options.TenantId is null)
        {
            throw new ArgumentException(
                "TenantId is required when ApplicationId is configured.",
                nameof(options));
        }
    }

    private static void ValidateEndpoint(Uri? value, bool allowInsecure, string name)
    {
        if (value is null
            || !value.IsAbsoluteUri
            || !string.IsNullOrEmpty(value.UserInfo)
            || !string.IsNullOrEmpty(value.Query)
            || !string.IsNullOrEmpty(value.Fragment))
        {
            throw new ArgumentException(
                "An absolute HTTP(S) endpoint without credentials, a query, or a fragment is required.",
                name);
        }

        if (string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!allowInsecure
            || !value.IsLoopback
            || !string.Equals(value.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Asterloom endpoints must use HTTPS. Plain HTTP is limited to explicitly enabled loopback development endpoints.",
                name);
        }
    }

    private static Uri EnsureTrailingSlash(Uri value)
    {
        if (value.AbsolutePath.EndsWith('/'))
        {
            return value;
        }

        var builder = new UriBuilder(value)
        {
            Path = value.AbsolutePath + "/",
        };
        return builder.Uri;
    }
}
