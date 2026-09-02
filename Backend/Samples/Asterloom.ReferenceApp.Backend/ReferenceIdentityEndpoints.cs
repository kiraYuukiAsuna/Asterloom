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
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        ReferenceIdentityGateway gateway,
        ReferenceIdentityOptions options,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var result = await gateway.Accounts.RegisterAccountAsync(
            request.Email,
            request.DisplayName,
            request.Password,
            cancellationToken);
        var mail = services.GetService<ReferenceMailGateway>();
        if (mail is not null && result.VerificationRequired)
        {
            await mail.SendAsync(
                request.Email,
                "Confirm your Reference App email",
                $"Your Reference App verification token is: {result.EmailVerificationToken}",
                $"<p>Your Reference App verification token is:</p><p><strong>{System.Net.WebUtility.HtmlEncode(result.EmailVerificationToken)}</strong></p>",
                $"registration:{result.User.Id:D}",
                cancellationToken);
        }

        return Results.Ok(new
        {
            user = ToResponse(result.User),
            membership = result.Membership,
            result.AccountCreated,
            result.VerificationRequired,
            // When Asterloom:Mail is enabled, the business backend submits this
            // content to Asterloom Mail. Development can still expose the token.
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
}
