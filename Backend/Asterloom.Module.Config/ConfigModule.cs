using Asterloom.Modules.Config.Persistence;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Config;

public sealed class ConfigModule : IAsterloomModule
{
    public string Name => "Config";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, ConfigInitialMigration>());
        services.AddScoped<ConfigDefinitionValidator>();
        services.AddScoped<ConfigEvaluationService>();
        services.AddScoped<ConfigRuntimeService>();
        services.AddScoped<ConfigManagementService>();
        services.AddScoped<ConfigAdminGrpcService>();
        services.AddScoped<ConfigGrpcService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<ConfigAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
        endpoints
            .MapGrpcService<ConfigGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
