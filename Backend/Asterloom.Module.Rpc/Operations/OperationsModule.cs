using Asterloom.Modules.Hosting;
using Asterloom.Modules.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.Modules.Rpc.Operations;

public sealed class OperationsModule : IAsterloomModule
{
    public string Name => "Operations";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<OperationsMetadataService>();
        services.AddScoped<OperationsAdminGrpcService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<OperationsAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
