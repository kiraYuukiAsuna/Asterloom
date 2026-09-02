using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Asterloom.Sdk.Identity.AspNetCore;

internal sealed partial class AsterloomPermissionAuthorizationHandler(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AsterloomPermissionAuthorizationHandler> logger)
    : AuthorizationHandler<AsterloomPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AsterloomPermissionRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null || context.User.Identity?.IsAuthenticated is not true)
        {
            return;
        }

        var accessToken = await httpContext.GetTokenAsync(
            AsterloomResourceServerDefaults.AuthenticationScheme,
            "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        var tenantId = context.User.FindFirstValue(
            AsterloomResourceServerDefaults.TenantIdClaim);
        var applicationId = context.User.FindFirstValue(
            AsterloomResourceServerDefaults.ApplicationIdClaim);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "api/v1/authorization:check")
        {
            Content = JsonContent.Create(new
            {
                actorId = string.Empty,
                scope = new
                {
                    tenantId = tenantId ?? string.Empty,
                    applicationId = applicationId ?? string.Empty,
                    environmentId = requirement.EnvironmentId?.ToString("D") ?? string.Empty,
                },
                permission = requirement.Permission,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var client = httpClientFactory.CreateClient(
                AsterloomResourceServerDefaults.AuthorizationHttpClientName);
            using var response = await client.SendAsync(request, httpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                LogPermissionCheckRejected(
                    logger,
                    requirement.Permission,
                    (int)response.StatusCode,
                    null);
                return;
            }

            var decision = await response.Content.ReadFromJsonAsync<AuthorizationDecision>(
                cancellationToken: httpContext.RequestAborted);
            if (decision?.Allowed is true)
            {
                context.Succeed(requirement);
            }
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is
            HttpRequestException or OperationCanceledException or JsonException)
        {
            LogPermissionCheckFailed(logger, requirement.Permission, exception);
        }
    }

    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Warning,
        Message = "Asterloom permission check for {Permission} returned HTTP {StatusCode}.")]
    private static partial void LogPermissionCheckRejected(
        ILogger logger,
        string permission,
        int statusCode,
        Exception? exception);

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Warning,
        Message = "Asterloom permission check for {Permission} could not reach the authorization service.")]
    private static partial void LogPermissionCheckFailed(
        ILogger logger,
        string permission,
        Exception exception);

    private sealed record AuthorizationDecision(bool Allowed);
}
