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

    private static readonly Action<ILogger, string, Exception?> LogUpdatedClient =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3003, nameof(LogUpdatedClient)),
            "Updated bootstrap OIDC client {ClientId} from current configuration.");

    private static readonly Action<ILogger, string, Exception?> LogRemovedPasswordGrant =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3004, nameof(LogRemovedPasswordGrant)),
            "Removed the disabled Password Grant permission from OIDC client {ClientId}.");

    public async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        await EnsureRolesAsync(cancellationToken);
        await EnsureApiScopeAsync(cancellationToken);
        await RemoveLegacyPasswordGrantPermissionsAsync(cancellationToken);
        await EnsureWebClientAsync(cancellationToken);
        await EnsureAdministratorAsync(cancellationToken);
    }

    private async Task RemoveLegacyPasswordGrantPermissionsAsync(
        CancellationToken cancellationToken)
    {
        await foreach (var application in applicationManager.ListAsync(
            cancellationToken: cancellationToken))
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await applicationManager.PopulateAsync(
                descriptor,
                application,
                cancellationToken);
            if (!descriptor.Permissions.Remove(Permissions.GrantTypes.Password))
            {
                continue;
            }

            await applicationManager.UpdateAsync(
                application,
                descriptor,
                cancellationToken);
            LogRemovedPasswordGrant(
                logger,
                descriptor.ClientId ?? "unknown",
                null);
        }
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
            var existingDescriptor = new OpenIddictApplicationDescriptor();
            await applicationManager.PopulateAsync(
                existingDescriptor,
                existing,
                cancellationToken);
            var expectedRedirectUris = new HashSet<Uri>
            {
                options.WebClientRedirectUri,
            };
            var expectedPostLogoutRedirectUris = new HashSet<Uri>();
            if (options.WebClientPostLogoutRedirectUri is not null)
            {
                expectedPostLogoutRedirectUris.Add(
                    options.WebClientPostLogoutRedirectUri);
            }

            var requiresUpdate = existingDescriptor.ApplicationType != ApplicationTypes.Web
                || !existingDescriptor.RedirectUris.SetEquals(expectedRedirectUris)
                || !existingDescriptor.PostLogoutRedirectUris.SetEquals(
                    expectedPostLogoutRedirectUris)
                || !IdentitySystemResourceMetadata.IsConfigurationManaged(existingDescriptor);
            if (!requiresUpdate)
            {
                return;
            }

            IdentitySystemResourceMetadata.MarkConfigurationManaged(existingDescriptor);
            existingDescriptor.ApplicationType = ApplicationTypes.Web;
            existingDescriptor.RedirectUris.Clear();
            existingDescriptor.RedirectUris.UnionWith(expectedRedirectUris);
            existingDescriptor.PostLogoutRedirectUris.Clear();
            existingDescriptor.PostLogoutRedirectUris.UnionWith(
                expectedPostLogoutRedirectUris);
            await applicationManager.UpdateAsync(
                existing,
                existingDescriptor,
                cancellationToken);
            LogUpdatedClient(logger, options.WebClientId, null);
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

        IdentitySystemResourceMetadata.MarkConfigurationManaged(descriptor);
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
