using Asterloom.Modules.Feature.Persistence;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Feature;

public sealed class FeatureModule : IAsterloomModule
{
    public string Name => "Feature";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, FeatureInitialMigration>());
        services.AddScoped<FeatureDefinitionValidator>();
        services.AddScoped<FeatureEvaluationService>();
        services.AddScoped<FeatureManagementService>();
        services.AddScoped<FeatureAdminGrpcService>();
        services.AddScoped<FeatureGrpcService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<FeatureAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
        endpoints
            .MapGrpcService<FeatureGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
