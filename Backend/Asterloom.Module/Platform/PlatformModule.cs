using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Platform.Persistence;
using Asterloom.Modules.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Platform;

public sealed class PlatformModule : IAsterloomModule
{
    public string Name => "Platform";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, PlatformInitialMigration>());
        services.AddSingleton<PlatformInfoProvider>();
        services.AddScoped<PlatformManagementService>();
        services.AddScoped<PlatformAdminGrpcService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<PlatformAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
