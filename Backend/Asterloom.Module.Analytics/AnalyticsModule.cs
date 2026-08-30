using System.Threading.RateLimiting;
using Asterloom.Modules.Analytics.Persistence;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Analytics;

public sealed class AnalyticsModule : IAsterloomModule
{
    public const string IngestionRateLimitPolicy = "analytics-ingestion";

    public string Name => "Analytics";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, AnalyticsInitialMigration>());
        services.AddScoped<AnalyticsManagementService>();
        services.AddScoped<AnalyticsIngestionService>();
        services.AddScoped<AnalyticsAdminGrpcService>();
        services.AddScoped<AnalyticsGrpcService>();
        services.AddHostedService<AnalyticsRetentionWorker>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                IngestionRateLimitPolicy,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Request.Headers["X-Asterloom-Write-Key"].FirstOrDefault()
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 600,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                    }));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<AnalyticsAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
        endpoints
            .MapGrpcService<AnalyticsGrpcService>()
            .AllowAnonymous()
            .RequireRateLimiting(IngestionRateLimitPolicy);
    }
}
