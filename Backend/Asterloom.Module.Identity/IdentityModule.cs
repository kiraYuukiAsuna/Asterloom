using Asterloom.Modules.Hosting;
using Asterloom.Modules.Identity.Bootstrap;
using Asterloom.Modules.Identity.Controllers;
using Asterloom.Modules.Identity.Management;
using Asterloom.Modules.Identity.Model;
using Asterloom.Modules.Identity.Persistence;
using Asterloom.Modules.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using System.Threading.RateLimiting;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.Modules.Identity;

public sealed class IdentityModule(IHostEnvironment environment) : IAsterloomModule
{
    public const string LoginRateLimitPolicy = "passport-login";
    public const string TokenRateLimitPolicy = "passport-token";

    public string Name => "Identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var loginPermitLimit = ReadPermitLimit(
            configuration,
            "Identity:RateLimiting:LoginPermitLimit",
            defaultValue: 10);
        var security = IdentitySecurityOptions.FromConfiguration(configuration, environment);
        services.AddSingleton(security);
        services.AddAsterloomIdentityCore(configuration);
        services.AddScoped<IdentityManagementService>();
        services.AddScoped<IdentityAdminGrpcService>();
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = IdentityManagementService.InvitationLifetime);
        var persistence = IdentityPersistenceOptions.FromConfiguration(configuration);
        if (persistence.Provider == IdentityPersistenceProvider.Memory)
        {
            services.AddHostedService<IdentityDevelopmentInitializer>();
        }

        Directory.CreateDirectory(security.DataProtectionKeysPath);
        services
            .AddDataProtection()
            .SetApplicationName("Asterloom.Passport")
            .PersistKeysToFileSystem(new DirectoryInfo(security.DataProtectionKeysPath));

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = security.IsDevelopment
                ? "Asterloom.Passport.Development"
                : "__Host-Asterloom.Passport";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = security.IsDevelopment
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.LoginPath = "/passport/login";
            options.AccessDeniedPath = "/passport/denied";
            options.ReturnUrlParameter = "returnUrl";
        });

        services.AddOpenIddict()
            .AddServer(options =>
            {
                options.SetIssuer(security.Issuer);
                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetEndSessionEndpointUris("/connect/logout")
                    .SetTokenEndpointUris("/connect/token")
                    .SetUserInfoEndpointUris("/connect/userinfo");
                options.AllowAuthorizationCodeFlow()
                    .AllowClientCredentialsFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange();
                options.RegisterScopes(
                    Scopes.Email,
                    Scopes.Profile,
                    Scopes.Roles,
                    "asterloom.api");
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5));
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));
                options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(5));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(14));
                options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);

                if (security.IsDevelopment)
                {
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    options.AddSigningCertificate(security.LoadSigningCertificate());
                    options.AddEncryptionCertificate(security.LoadEncryptionCertificate());
                }

                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
                if (security.IsDevelopment)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(
                AsterloomApiAuthorization.ManagementPolicy,
                policy =>
                {
                    policy.AuthenticationSchemes.Add(
                        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(context =>
                        context.User.HasScope(AsterloomApiAuthorization.ApiScope));
                });

        services.AddAntiforgery(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.Name = security.IsDevelopment
                ? "Asterloom.Passport.Antiforgery.Development"
                : "__Host-Asterloom.Passport.Antiforgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = security.IsDevelopment
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.FormFieldName = "__RequestVerificationToken";
            options.HeaderName = "X-CSRF-TOKEN";
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                LoginRateLimitPolicy,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = loginPermitLimit,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                    }));
            options.AddPolicy(
                TokenRateLimitPolicy,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                    }));
        });
        services
            .AddControllersWithViews()
            .AddApplicationPart(typeof(PassportController).Assembly);
    }

    private static int ReadPermitLimit(
        IConfiguration configuration,
        string key,
        int defaultValue)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var permitLimit)
            || permitLimit is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be an integer between 1 and 10,000.");
        }

        return permitLimit;
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllers();
        endpoints
            .MapGrpcService<IdentityAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
