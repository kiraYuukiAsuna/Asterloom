using System.Security.Claims;
using Asterloom.Sdk.Authorization;
using Asterloom.Targeting;
using Microsoft.AspNetCore.Mvc;

namespace Asterloom.ReferenceApp.Backend;

internal static class ReferenceBusinessAuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapReferenceBusinessAuthorizationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reference/authorization")
            .RequireAuthorization();
        group.MapPost("/fixture", SeedFixtureAsync);
        group.MapPost("/orders/{orderId}/refund", AuthorizeRefundAsync);
        return endpoints;
    }

    private static async Task<IResult> SeedFixtureAsync(
        AuthorizationFixtureRequest request,
        ClaimsPrincipal user,
        [FromServices] ReferenceAppStore store,
        CancellationToken cancellationToken)
    {
        var subjectId = RequireSubject(user);
        var department = RequireText(request.Department, nameof(request.Department), 100);
        var orderId = RequireText(request.OrderId, nameof(request.OrderId), 200);
        if (!double.IsFinite(request.Amount) || request.Amount < 0)
        {
            return Results.BadRequest(new { error = "Amount must be a finite non-negative number." });
        }

        await store.SeedAuthorizationFixtureAsync(
            subjectId,
            department,
            orderId,
            request.Amount,
            cancellationToken);
        return Results.Ok(new { subjectId, department, orderId, request.Amount });
    }

    private static async Task<IResult> AuthorizeRefundAsync(
        string orderId,
        ClaimsPrincipal user,
        [FromServices] ReferenceAppStore store,
        [FromServices] ReferenceIdentityGateway gateway,
        [FromServices] ReferenceResourceServerOptions options,
        CancellationToken cancellationToken)
    {
        var subjectId = RequireSubject(user);
        var authorizationContext = await store.GetAuthorizationContextAsync(
            subjectId,
            orderId,
            cancellationToken);
        if (authorizationContext is null)
        {
            return Results.NotFound(new { error = "The order or authorization profile was not found." });
        }

        // These attributes come from business-owned persistence and the validated token,
        // never from caller-provided authorization fields.
        var attributes = new Dictionary<string, TargetingValue>(StringComparer.Ordinal)
        {
            ["subject.department"] = TargetingValue.From(authorizationContext.Department),
            ["resource.amount"] = TargetingValue.From(authorizationContext.Amount),
            ["resource.ownerId"] = TargetingValue.From(authorizationContext.OwnerSubjectId),
            ["context.mfa"] = TargetingValue.From(
                user.FindAll("amr").Any(claim =>
                    string.Equals(claim.Value, "mfa", StringComparison.OrdinalIgnoreCase))),
        };
        var decision = await gateway.Authorization.CheckAccessAsync(
            subjectId,
            "orders.refund",
            new AsterloomAuthorizationScope(options.TenantId, options.ApplicationId),
            "order",
            authorizationContext.OrderId,
            attributes,
            cancellationToken);
        if (!decision.Allowed)
        {
            return Results.Json(
                new { error = "forbidden", decision.Reason },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(new
        {
            refunded = true,
            orderId = authorizationContext.OrderId,
            decision.Reason,
            decision.MatchedRoleKeys,
            decision.MatchedPolicyIds,
        });
    }

    private static string RequireSubject(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") is { Length: > 0 } subjectId
            ? subjectId
            : throw new InvalidOperationException("The validated token has no subject claim.");

    private static string RequireText(string value, string name, int maximumLength)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximumLength)
        {
            throw new BadHttpRequestException(
                $"{name} must contain between 1 and {maximumLength} characters.");
        }

        return normalized;
    }

    private sealed record AuthorizationFixtureRequest(
        string Department,
        string OrderId,
        double Amount);
}
