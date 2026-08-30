using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Release.Persistence;
using Asterloom.Modules.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Release;

public sealed class ReleaseModule : IAsterloomModule
{
    public string Name => "Release";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, ReleaseInitialMigration>());
        services.AddScoped<ReleaseManagementService>();
        services.AddScoped<ReleaseEvaluationService>();
        services.AddScoped<ReleaseAdminGrpcService>();
        services.AddScoped<ReleaseGrpcService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<ReleaseAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
        endpoints
            .MapGrpcService<ReleaseGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
