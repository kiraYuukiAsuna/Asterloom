using System.Security.Claims;
using Asterloom.Modules.Identity.Model;
using Asterloom.Modules.Identity.Management;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using OidcErrors = OpenIddict.Abstractions.OpenIddictConstants.Errors;

namespace Asterloom.Modules.Identity.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ConnectController(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictScopeManager scopeManager,
    IdentityMembershipService memberships,
    SignInManager<AsterloomUser> signInManager,
    UserManager<AsterloomUser> userManager) : Controller
{
    [HttpGet("/connect/authorize")]
    [HttpPost("/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The OpenID Connect authorization request is unavailable.");
        var authentication = await HttpContext.AuthenticateAsync(
            IdentityConstants.ApplicationScheme);
        if (RequiresLogin(request, authentication))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return OpenIddictForbid(
                    OidcErrors.LoginRequired,
                    "The user is not logged in.");
            }

            var continuation = Request.HasFormContentType
                ? QueryString.Create(Request.Form)
                : Request.QueryString;
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + continuation,
                },
                IdentityConstants.ApplicationScheme);
        }

        var user = await userManager.GetUserAsync(authentication.Principal!);
        if (!IsActive(user))
        {
            await signInManager.SignOutAsync();
            return OpenIddictForbid(
                OidcErrors.LoginRequired,
                "The user account is not available.");
        }

        var application = await applicationManager.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException(
                "The OpenID Connect client cannot be found.");
        var binding = await IdentityClientApplicationMetadata.ReadAsync(
            applicationManager,
            application,
            HttpContext.RequestAborted);
        if (!await EnsureApplicationAccessAsync(
            user!,
            binding,
            allowAutoJoin: binding?.AllowMembershipAutoJoin is true,
            HttpContext.RequestAborted))
        {
            return OpenIddictForbid(
                OidcErrors.AccessDenied,
                "The account is not a member of this application.");
        }
        var subject = await userManager.GetUserIdAsync(user!);
        var applicationId = await applicationManager.GetIdAsync(application)
            ?? throw new InvalidOperationException(
                "The OpenID Connect client has no stable identifier.");
        var authorizations = await authorizationManager.FindAsync(
            subject,
            applicationId,
            Statuses.Valid,
            AuthorizationTypes.Permanent,
            request.GetScopes()).ToListAsync();

        var consentType = await applicationManager.GetConsentTypeAsync(application);
        if (consentType == ConsentTypes.External && authorizations.Count == 0)
        {
            return OpenIddictForbid(
                OidcErrors.ConsentRequired,
                "The user is not allowed to use this client application.");
        }

        if (consentType is ConsentTypes.Explicit or ConsentTypes.Systematic
            && (authorizations.Count == 0
                || request.HasPromptValue(PromptValues.Consent)))
        {
            return OpenIddictForbid(
                OidcErrors.ConsentRequired,
                "Interactive consent is required for this client application.");
        }

        var identity = await CreateUserIdentityAsync(user!, request.GetScopes(), binding);
        var authorization = authorizations.LastOrDefault();
        if (authorization is null && consentType == ConsentTypes.Implicit)
        {
            authorization = await authorizationManager.CreateAsync(
                identity,
                subject,
                applicationId,
                AuthorizationTypes.Permanent,
                identity.GetScopes());
        }

        if (authorization is not null)
        {
            identity.SetAuthorizationId(
                await authorizationManager.GetIdAsync(authorization));
        }

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    [EnableRateLimiting(IdentityModule.TokenRateLimitPolicy)]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The OAuth 2.0 token request is unavailable.");

        if (request.IsAuthorizationCodeGrantType()
            || request.IsRefreshTokenGrantType())
        {
            var authentication = await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = authentication.Principal?.GetClaim(Claims.Subject);
            var user = string.IsNullOrWhiteSpace(subject)
                ? null
                : await userManager.FindByIdAsync(subject);
            if (!IsActive(user) || !await signInManager.CanSignInAsync(user!))
            {
                return OpenIddictForbid(
                    OidcErrors.InvalidGrant,
                    "The authorization grant is no longer valid.");
            }

            var application = await applicationManager.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException(
                    "The OAuth 2.0 client cannot be found.");
            var binding = await IdentityClientApplicationMetadata.ReadAsync(
                applicationManager,
                application,
                HttpContext.RequestAborted);
            if (!await EnsureApplicationAccessAsync(
                user!,
                binding,
                allowAutoJoin: false,
                HttpContext.RequestAborted))
            {
                return OpenIddictForbid(
                    OidcErrors.InvalidGrant,
                    "The account is no longer a member of this application.");
            }

            var identity = await CreateUserIdentityAsync(
                user!,
                authentication.Principal!.GetScopes(),
                binding);
            var principal = authentication.Principal!;
            identity.SetAuthorizationId(principal.GetAuthorizationId());
            return SignIn(
                new ClaimsPrincipal(identity),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsPasswordGrantType())
        {
            var application = await applicationManager.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException(
                    "The OAuth 2.0 client cannot be found.");
            var binding = await IdentityClientApplicationMetadata.ReadAsync(
                applicationManager,
                application,
                HttpContext.RequestAborted);
            if (binding is null)
            {
                return OpenIddictForbid(
                    OidcErrors.UnauthorizedClient,
                    "Password authentication requires an application-bound client.");
            }

            var user = string.IsNullOrWhiteSpace(request.Username)
                ? null
                : await userManager.FindByEmailAsync(request.Username.Trim());
            if (!IsActive(user) || !await signInManager.CanSignInAsync(user!))
            {
                return OpenIddictForbid(
                    OidcErrors.InvalidGrant,
                    "The email or password is invalid.");
            }

            var password = await signInManager.CheckPasswordSignInAsync(
                user!,
                request.Password ?? string.Empty,
                lockoutOnFailure: true);
            if (!password.Succeeded)
            {
                return OpenIddictForbid(
                    OidcErrors.InvalidGrant,
                    "The email or password is invalid.");
            }

            if (!await EnsureApplicationAccessAsync(
                user!,
                binding,
                allowAutoJoin: binding.AllowMembershipAutoJoin,
                HttpContext.RequestAborted))
            {
                return OpenIddictForbid(
                    OidcErrors.InvalidGrant,
                    "The account is not a member of this application.");
            }

            var identity = await CreateUserIdentityAsync(
                user!,
                request.GetScopes(),
                binding);
            var subject = await userManager.GetUserIdAsync(user!);
            var applicationId = await applicationManager.GetIdAsync(application)
                ?? throw new InvalidOperationException(
                    "The OAuth 2.0 client has no stable identifier.");
            var authorization = await authorizationManager.CreateAsync(
                identity,
                subject,
                applicationId,
                AuthorizationTypes.Permanent,
                identity.GetScopes());
            identity.SetAuthorizationId(
                await authorizationManager.GetIdAsync(authorization));
            return SignIn(
                new ClaimsPrincipal(identity),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            var application = await applicationManager.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException(
                    "The OAuth 2.0 client cannot be found.");
            var displayName = await applicationManager.GetDisplayNameAsync(application)
                ?? request.ClientId!;
            var binding = await IdentityClientApplicationMetadata.ReadAsync(
                applicationManager,
                application,
                HttpContext.RequestAborted);
            var identity = new ClaimsIdentity(
                TokenValidationParameters.DefaultAuthenticationType,
                Claims.Name,
                Claims.Role);
            identity.SetClaim(Claims.Subject, request.ClientId)
                .SetClaim(Claims.Name, displayName)
                .SetClaim(Claims.ClientId, request.ClientId)
                .SetClaim(IdentityClaimTypes.ActorType, IdentityClaimTypes.ClientActor)
                .SetScopes(request.GetScopes());
            SetApplicationClaims(identity, binding);
            identity.SetDestinations(GetDestinations);
            await SetResourcesAsync(identity);
            return SignIn(
                new ClaimsPrincipal(identity),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return OpenIddictForbid(
            OidcErrors.UnsupportedGrantType,
            "The specified grant type is not supported.");
    }

    [Authorize(AuthenticationSchemes =
        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    [HttpGet("/connect/userinfo")]
    [HttpPost("/connect/userinfo")]
    [Produces("application/json")]
    public IActionResult UserInfo()
    {
        var response = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = User.GetClaim(Claims.Subject)
                ?? throw new InvalidOperationException("The subject claim is missing."),
        };

        AddClaimIfPresent(response, Claims.Name);
        AddClaimIfPresent(response, Claims.PreferredUsername);
        AddClaimIfPresent(response, Claims.Email);
        AddClaimIfPresent(response, IdentityClaimTypes.TenantId);
        AddClaimIfPresent(response, IdentityClaimTypes.ApplicationId);
        var roles = User.GetClaims(Claims.Role).ToArray();
        if (roles.Length > 0)
        {
            response[Claims.Role] = roles;
        }

        return Ok(response);
    }

    [HttpGet("/connect/logout")]
    [HttpPost("/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        await signInManager.SignOutAsync();
        return SignOut(
            new AuthenticationProperties
            {
                RedirectUri = request?.PostLogoutRedirectUri ?? "/",
            },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<ClaimsIdentity> CreateUserIdentityAsync(
        AsterloomUser user,
        IEnumerable<string> scopes,
        IdentityClientApplicationBinding? binding)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user))
            .SetClaim(Claims.Email, await userManager.GetEmailAsync(user))
            .SetClaim(Claims.Name, user.DisplayName)
            .SetClaim(Claims.PreferredUsername, await userManager.GetUserNameAsync(user))
            .SetClaim(IdentityClaimTypes.ActorType, IdentityClaimTypes.UserActor)
            .SetClaims(Claims.Role, [.. await userManager.GetRolesAsync(user)])
            .SetScopes(scopes);
        SetApplicationClaims(identity, binding);
        identity.SetDestinations(GetDestinations);
        await SetResourcesAsync(identity);
        return identity;
    }

    private async Task<bool> EnsureApplicationAccessAsync(
        AsterloomUser user,
        IdentityClientApplicationBinding? binding,
        bool allowAutoJoin,
        CancellationToken cancellationToken)
    {
        if (binding is null)
        {
            return true;
        }

        var membership = await memberships.FindAsync(
            user.Id,
            binding.ApplicationId,
            cancellationToken);
        if (membership is
            {
                Status: AsterloomApplicationMembershipStatus.Active,
            })
        {
            return true;
        }

        if (!allowAutoJoin)
        {
            return false;
        }

        await memberships.SetAsync(
            user.Id,
            binding.TenantId,
            binding.ApplicationId,
            membership?.Version ?? 0,
            cancellationToken);
        return true;
    }

    private static void SetApplicationClaims(
        ClaimsIdentity identity,
        IdentityClientApplicationBinding? binding)
    {
        if (binding is null)
        {
            return;
        }

        identity.SetClaim(IdentityClaimTypes.TenantId, binding.TenantId.ToString("D"));
        identity.SetClaim(
            IdentityClaimTypes.ApplicationId,
            binding.ApplicationId.ToString("D"));
    }

    private async Task SetResourcesAsync(ClaimsIdentity identity)
    {
        var resources = await scopeManager
            .ListResourcesAsync(identity.GetScopes())
            .ToListAsync();
        identity.SetResources(resources);
    }

    private static bool RequiresLogin(
        OpenIddictRequest request,
        AuthenticateResult authentication)
    {
        if (!authentication.Succeeded)
        {
            return true;
        }

        if (request.HasPromptValue(PromptValues.Login) || request.MaxAge is 0)
        {
            return true;
        }

        return request.MaxAge is not null
            && authentication.Properties?.IssuedUtc is not null
            && TimeProvider.System.GetUtcNow() - authentication.Properties.IssuedUtc
                > TimeSpan.FromSeconds(request.MaxAge.Value);
    }

    private static bool IsActive(AsterloomUser? user) =>
        user is
        {
            Status: AsterloomUserStatus.Active,
            ArchivedAt: null,
            EmailConfirmed: true,
        };

    private static ForbidResult OpenIddictForbid(string error, string description) =>
        new(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }));

    private void AddClaimIfPresent(Dictionary<string, object> response, string claimType)
    {
        var value = User.GetClaim(claimType);
        if (!string.IsNullOrWhiteSpace(value))
        {
            response[claimType] = value;
        }
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Name or Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Profile))
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Email))
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Roles))
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
