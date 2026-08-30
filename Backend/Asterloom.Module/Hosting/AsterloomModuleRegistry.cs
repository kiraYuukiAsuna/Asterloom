using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Hosting;

/// <summary>
/// Stores and applies the explicitly selected modules for the server host.
/// </summary>
public sealed class AsterloomModuleRegistry
{
    private readonly IReadOnlyList<IAsterloomModule> _modules;

    public AsterloomModuleRegistry(IEnumerable<IAsterloomModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        _modules = modules.ToArray();

        var duplicate = _modules
            .GroupBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"The Asterloom module name '{duplicate.Key}' is registered more than once.");
        }
    }

    public IReadOnlyList<IAsterloomModule> Modules => _modules;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        foreach (var module in _modules)
        {
            module.MapEndpoints(endpoints);
        }
    }
}

public static class AsterloomModuleHostingExtensions
{
    public static IServiceCollection AddAsterloomModules(
        this IServiceCollection services,
        IConfiguration configuration,
        params IAsterloomModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);

        var registry = new AsterloomModuleRegistry(modules);

        foreach (var module in registry.Modules)
        {
            module.AddServices(services, configuration);
        }

        services.TryAddSingleton(registry);
        return services;
    }

    public static IEndpointRouteBuilder MapAsterloomModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var registry = endpoints.ServiceProvider.GetRequiredService<AsterloomModuleRegistry>();
        registry.MapEndpoints(endpoints);
        return endpoints;
    }
}
