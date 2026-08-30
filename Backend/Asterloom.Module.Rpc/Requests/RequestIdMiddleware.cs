using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Asterloom.Modules.Rpc.Requests;

public sealed class RequestIdMiddleware(
    RequestDelegate next,
    ILogger<RequestIdMiddleware> logger)
{
    public const string HeaderName = "X-Request-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var suppliedRequestId = context.Request.Headers[HeaderName].ToString();
        var requestId = IsValid(suppliedRequestId)
            ? suppliedRequestId
            : CreateRequestId();

        context.TraceIdentifier = requestId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = requestId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["RequestId"] = requestId,
            });
        await next(context);
    }

    private static bool IsValid(string value)
    {
        if (value.Length is < 8 or > 64)
        {
            return false;
        }

        return value.All(static character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':');
    }

    private static string CreateRequestId()
    {
        var activity = Activity.Current;
        return activity is not null && activity.TraceId != default
            ? activity.TraceId.ToString()
            : Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
    }
}
