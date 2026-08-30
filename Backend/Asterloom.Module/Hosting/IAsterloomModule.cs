using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.Modules.Hosting;

/// <summary>
/// Defines the composition boundary implemented by every Asterloom module.
/// </summary>
public interface IAsterloomModule
{
    /// <summary>
    /// Gets the stable module name used by diagnostics and tooling.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Registers this module's services.
    /// </summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Maps this module's transport endpoints.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
