using Asterloom.Modules.Hosting;
using Asterloom.Modules.Analytics;
using Asterloom.Modules.Auditing;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Config;
using Asterloom.Modules.Feature;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Infrastructure.Persistence;
using Asterloom.Modules.Identity.Persistence;
using Asterloom.Modules.Identity.Bootstrap;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Release;
using Asterloom.Modules.Storage;
using Asterloom.Modules.Targeting;
using Asterloom.Modules.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddAsterloomModules(
        builder.Configuration,
        new PlatformModule(),
        new AuthorizationModule(),
        new AuditModule(),
        new TargetingModule(),
        new FeatureModule(),
        new ConfigModule(),
        new StorageModule(),
        new ReleaseModule(),
        new AnalyticsModule(),
        new TelemetryModule(),
        new InfrastructureModule());
    builder.Services.AddAsterloomIdentityCore(builder.Configuration);

    using var host = builder.Build();
    var options = host.Services.GetRequiredService<AsterloomPersistenceOptions>();
    if (options.Provider != AsterloomPersistenceProvider.PostgreSql)
    {
        Console.Error.WriteLine(
            "Database migrations require Persistence:Provider=PostgreSql.");
        return 2;
    }

    var migrator = host.Services.GetRequiredService<IAsterloomDatabaseMigrator>();
    var result = await migrator.MigrateAsync(CancellationToken.None);
    await using (var scope = host.Services.CreateAsyncScope())
    {
        var identityMigrator = scope.ServiceProvider
            .GetRequiredService<IIdentityDatabaseMigrator>();
        await identityMigrator.MigrateAsync(CancellationToken.None);
        var identityBootstrapper = scope.ServiceProvider
            .GetRequiredService<IIdentityBootstrapper>();
        await identityBootstrapper.BootstrapAsync(CancellationToken.None);
    }
    Console.WriteLine(
        $"Database migrations complete, including Identity. " +
        $"Applied: {result.AppliedCount}; " +
        $"already applied: {result.PreviouslyAppliedCount}.");
    return 0;
}
catch (Exception exception) when (
    exception is InvalidOperationException or Npgsql.NpgsqlException)
{
    Console.Error.WriteLine($"Database migration failed: {exception.Message}");
    return 1;
}
