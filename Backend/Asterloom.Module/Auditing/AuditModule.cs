using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Auditing;

public sealed class AuditModule : IAsterloomModule
{
    public string Name => "Audit";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, AuditInitialMigration>());
        services.AddScoped<AuditManagementService>();
        services.AddScoped<AuditAdminGrpcService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<AuditAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
