using System.Security.Claims;
using System.Threading.RateLimiting;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Mail.Persistence;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Mail;

public sealed class MailModule : IAsterloomModule
{
    public const string DeliveryRateLimitPolicy = "mail-delivery";

    public string Name => "Mail";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection().SetApplicationName("Asterloom");
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, MailInitialMigration>());
        services.AddScoped<MailAccountManagementService>();
        services.AddScoped<MailDeliveryService>();
        services.AddScoped<MailAdminGrpcService>();
        services.AddScoped<MailGrpcService>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                DeliveryRateLimitPolicy,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue("sub")
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                    }));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<MailAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
        endpoints
            .MapGrpcService<MailGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy)
            .RequireRateLimiting(DeliveryRateLimitPolicy);
    }
}
