using System.Security.Claims;
using Asterloom.Sdk.Identity;

namespace Asterloom.ReferenceApp.Backend;

internal static class ReferenceIdentityEndpoints
{
    public static IEndpointRouteBuilder MapReferenceIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reference/account");
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/confirm-email", ConfirmEmailAsync);
        group.MapPost("/login", LoginAsync);
        group.MapGet("/me", GetCurrentAsync);
        group.MapPost("/logout", Logout);
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        ReferenceIdentityGateway gateway,
        ReferenceIdentityOptions options,
        CancellationToken cancellationToken)
    {
        var result = await gateway.Accounts.RegisterAccountAsync(
            request.Email,
            request.DisplayName,
            request.Password,
            cancellationToken);
        return Results.Ok(new
        {
            user = ToResponse(result.User),
            membership = result.Membership,
            result.AccountCreated,
            result.VerificationRequired,
            // A real business backend sends this through its own email provider.
            // The reference app exposes it only behind an explicit development flag.
            emailVerificationToken = options.ExposeEmailVerificationToken
                ? result.EmailVerificationToken
                : null,
        });
    }

    private static async Task<IResult> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        ReferenceIdentityGateway gateway,
        CancellationToken cancellationToken)
    {
        var user = await gateway.Accounts.ConfirmEmailAsync(
            request.Email,
            request.Token,
            cancellationToken);
        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        ReferenceIdentityGateway gateway,
        ReferenceIdentitySessionStore sessions,
        CancellationToken cancellationToken)
    {
        var tokens = await gateway.AuthenticateAsync(
            request.Email,
            request.Password,
            cancellationToken);
        var sessionId = sessions.Create(tokens);
        context.Response.Cookies.Append(
            ReferenceIdentitySessionStore.CookieName,
            sessionId,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = TimeSpan.FromHours(8),
                Path = "/",
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
            });
        return Results.Ok(ToSessionResponse(tokens));
    }

    private static async Task<IResult> GetCurrentAsync(
        HttpContext context,
        ReferenceIdentitySessionStore sessions,
        CancellationToken cancellationToken)
    {
        var tokens = await sessions.GetAsync(
            context.Request.Cookies[ReferenceIdentitySessionStore.CookieName],
            cancellationToken);
        return tokens is null
            ? Results.Unauthorized()
            : Results.Ok(ToSessionResponse(tokens));
    }

    private static IResult Logout(
        HttpContext context,
        ReferenceIdentitySessionStore sessions)
    {
        sessions.Remove(context.Request.Cookies[ReferenceIdentitySessionStore.CookieName]);
        context.Response.Cookies.Delete(
            ReferenceIdentitySessionStore.CookieName,
            new CookieOptions { Path = "/" });
        return Results.NoContent();
    }

    private static object ToSessionResponse(AsterloomTokenSet tokens) => new
    {
        subject = tokens.Principal.FindFirstValue("sub"),
        email = tokens.Principal.FindFirstValue("email"),
        name = tokens.Principal.FindFirstValue("name"),
        tenantId = tokens.Principal.FindFirstValue("tenant_id"),
        applicationId = tokens.Principal.FindFirstValue("application_id"),
        tokens.AccessTokenExpiresAt,
    };

    private static object ToResponse(AsterloomIdentityUser user) => new
    {
        user.Id,
        user.Email,
        user.DisplayName,
        status = user.Status.ToString(),
        user.EmailConfirmed,
    };

    private sealed record RegisterRequest(string Email, string DisplayName, string Password);

    private sealed record ConfirmEmailRequest(string Email, string Token);

    private sealed record LoginRequest(string Email, string Password);
}
