using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Security;
using Asterloom.Modules.Targeting.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Targeting;

public sealed class TargetingModule : IAsterloomModule
{
    public string Name => "Targeting";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, TargetingInitialMigration>());
        services.AddScoped<TargetingManagementService>();
        services.AddScoped<TargetingAdminGrpcService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<TargetingAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
