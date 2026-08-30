using Asterloom.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Asterloom.Modules.Identity.Bootstrap;

internal sealed class IdentityDevelopmentInitializer(
    IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IIdentityDatabaseMigrator>();
        await migrator.MigrateAsync(cancellationToken);
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IIdentityBootstrapper>();
        await bootstrapper.BootstrapAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
