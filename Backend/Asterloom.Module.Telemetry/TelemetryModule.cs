using Asterloom.Modules.Diagnostics;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Security;
using Asterloom.Modules.Telemetry.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Telemetry;

public sealed class TelemetryModule : IAsterloomModule
{
    public string Name => "Telemetry";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(TelemetryManagementOptions.FromConfiguration(configuration));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, TelemetryInitialMigration>());
        services.AddHttpClient<TelemetryCollectorHealthProbe>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(2);
        });
        services.AddScoped<TelemetryManagementService>();
        services.AddScoped<TelemetryAdminGrpcService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITechnicalDiagnosticSink, TelemetryDiagnosticSink>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<TelemetryAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);
    }
}
