using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Security;
using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Authorization;

public sealed class AuthorizationModule : IAsterloomModule
{
    public string Name => "Authorization";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration,
                AuthorizationInitialMigration>());
        services.AddSingleton<AuthorizationDecisionService>();
        services.AddScoped<AuthorizationManagementService>();
        services.AddScoped<AuthorizationAdminGrpcService>();
        services.AddScoped<AuthorizationGrpcService>();
        services.AddSingleton<AuthorizationInterceptor>();
        services.Configure<GrpcServiceOptions>(options =>
            options.Interceptors.Add<AuthorizationInterceptor>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<AuthorizationAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
        endpoints
            .MapGrpcService<AuthorizationGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
