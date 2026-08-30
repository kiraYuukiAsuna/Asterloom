using Asterloom.Modules.Identity.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.Modules.Identity.Bootstrap;

public interface IIdentityBootstrapper
{
    Task BootstrapAsync(CancellationToken cancellationToken);
}

internal sealed class IdentityBootstrapper(
    IdentityBootstrapOptions options,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<AsterloomUser> userManager,
    TimeProvider timeProvider,
    ILogger<IdentityBootstrapper> logger) : IIdentityBootstrapper
{
    private static readonly Action<ILogger, string, Exception?> LogCreatedClient =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3001, nameof(LogCreatedClient)),
            "Created bootstrap OIDC client {ClientId}.");

    private static readonly Action<ILogger, string, Exception?> LogCreatedAdmin =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3002, nameof(LogCreatedAdmin)),
            "Created bootstrap administrator {AdminEmail}.");

    public async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        await EnsureRolesAsync(cancellationToken);
        await EnsureApiScopeAsync(cancellationToken);
        await EnsureWebClientAsync(cancellationToken);
        await EnsureAdministratorAsync(cancellationToken);
    }

    private async Task EnsureRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var roleName in IdentityRoleCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                EnsureSucceeded(
                    await roleManager.CreateAsync(new IdentityRole<Guid>(roleName)),
                    $"create the trusted Passport role {roleName}");
            }
        }
    }

    private async Task EnsureApiScopeAsync(CancellationToken cancellationToken)
    {
        if (await scopeManager.FindByNameAsync("asterloom.api", cancellationToken) is not null)
        {
            return;
        }

        await scopeManager.CreateAsync(
            new OpenIddictScopeDescriptor
            {
                Name = "asterloom.api",
                DisplayName = "Asterloom API",
                Resources = { "asterloom-api" },
            },
            cancellationToken);
    }

    private async Task EnsureWebClientAsync(CancellationToken cancellationToken)
    {
        if (options.WebClientId is null)
        {
            return;
        }

        if (options.WebClientRedirectUri is null)
        {
            throw new InvalidOperationException(
                "Identity WebClient configuration requires RedirectUri when ClientId is set.");
        }

        var existing = await applicationManager.FindByClientIdAsync(
            options.WebClientId,
            cancellationToken);
        if (existing is not null)
        {
            if (!await applicationManager.HasApplicationTypeAsync(
                    existing,
                    ApplicationTypes.Web,
                    cancellationToken))
            {
                var existingDescriptor = new OpenIddictApplicationDescriptor();
                await applicationManager.PopulateAsync(
                    existingDescriptor,
                    existing,
                    cancellationToken);
                existingDescriptor.ApplicationType = ApplicationTypes.Web;
                await applicationManager.UpdateAsync(
                    existing,
                    existingDescriptor,
                    cancellationToken);
            }

            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = options.WebClientId,
            ClientSecret = options.WebClientSecret,
            ClientType = options.WebClientSecret is null
                ? ClientTypes.Public
                : ClientTypes.Confidential,
            ApplicationType = ApplicationTypes.Web,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Asterloom Web Console",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.EndSession,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + "asterloom.api",
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange,
            },
            RedirectUris =
            {
                options.WebClientRedirectUri,
            },
        };
        if (options.WebClientPostLogoutRedirectUri is not null)
        {
            descriptor.PostLogoutRedirectUris.Add(
                options.WebClientPostLogoutRedirectUri);
        }

        await applicationManager.CreateAsync(descriptor, cancellationToken);
        LogCreatedClient(logger, options.WebClientId, null);
    }

    private async Task EnsureAdministratorAsync(CancellationToken cancellationToken)
    {
        if (options.AdminEmail is null || options.AdminPassword is null)
        {
            return;
        }

        var user = await userManager.FindByEmailAsync(options.AdminEmail);
        if (user is null)
        {
            var now = timeProvider.GetUtcNow();
            user = new AsterloomUser
            {
                Id = Guid.NewGuid(),
                UserName = options.AdminEmail,
                Email = options.AdminEmail,
                EmailConfirmed = true,
                DisplayName = options.AdminDisplayName,
                Status = AsterloomUserStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            };
            EnsureSucceeded(
                await userManager.CreateAsync(user, options.AdminPassword),
                "create the bootstrap administrator");
            LogCreatedAdmin(logger, options.AdminEmail, null);
        }

        if (!await userManager.IsInRoleAsync(
            user,
            IdentityRoleCatalog.SuperAdministrator))
        {
            EnsureSucceeded(
                await userManager.AddToRoleAsync(
                    user,
                    IdentityRoleCatalog.SuperAdministrator),
                "assign the bootstrap administrator role");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(
            "; ",
            result.Errors.Select(static error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Unable to {operation}: {errors}");
    }
}
