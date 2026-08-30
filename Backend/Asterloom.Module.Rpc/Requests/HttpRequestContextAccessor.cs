using System.Security.Claims;
using Asterloom.Modules.Requests;
using Microsoft.AspNetCore.Http;

namespace Asterloom.Modules.Rpc.Requests;

internal sealed class HttpRequestContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IAsterloomRequestContextAccessor
{
    public AsterloomRequestContext? Current
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext is null)
            {
                return null;
            }

            return new AsterloomRequestContext(
                httpContext.TraceIdentifier,
                httpContext.User.FindFirstValue("sub")
                    ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier),
                ReadRouteValue(httpContext, "tenant_id"),
                ReadRouteValue(httpContext, "application_id"),
                ReadRouteValue(httpContext, "environment_id"));
        }
    }

    private static string? ReadRouteValue(HttpContext context, string name) =>
        context.Request.RouteValues.TryGetValue(name, out var value)
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
}
