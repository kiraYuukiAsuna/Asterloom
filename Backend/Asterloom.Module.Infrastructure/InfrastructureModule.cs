using Asterloom.Modules.Hosting;
using Asterloom.Modules.Analytics.Persistence;
using Asterloom.Modules.Infrastructure.Analytics;
using Asterloom.Modules.Auditing;
using Asterloom.Modules.Infrastructure.Auditing;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Modules.Config.Persistence;
using Asterloom.Modules.Infrastructure.Config;
using Asterloom.Modules.Infrastructure.Authorization;
using Asterloom.Modules.Feature.Persistence;
using Asterloom.Modules.Infrastructure.Feature;
using Asterloom.Modules.Infrastructure.Mail;
using Asterloom.Modules.Infrastructure.Persistence;
using Asterloom.Modules.Infrastructure.Outbox;
using Asterloom.Modules.Outbox;
using Asterloom.Modules.Mail.Persistence;
using Asterloom.Modules.Mail.Transport;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Infrastructure.Platform;
using Asterloom.Modules.Platform.Persistence;
using Asterloom.Modules.Infrastructure.Targeting;
using Asterloom.Modules.Targeting.Persistence;
using Asterloom.Modules.Infrastructure.Storage;
using Asterloom.Modules.Infrastructure.Release;
using Asterloom.Modules.Release.Persistence;
using Asterloom.Modules.Storage.Persistence;
using Asterloom.Modules.Storage.Transport;
using Asterloom.Modules.Infrastructure.Telemetry;
using Asterloom.Modules.Telemetry.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Asterloom.Modules.Infrastructure;

public sealed class InfrastructureModule : IAsterloomModule
{
    public string Name => "Infrastructure";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var options = AsterloomPersistenceOptions.FromConfiguration(configuration);
        var outboxOptions = OutboxDispatcherOptions.FromConfiguration(configuration);
        services.AddSingleton(options);
        services.AddSingleton(outboxOptions);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration,
                InfrastructureOutboxInitialMigration>());
        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();

        if (options.Provider == AsterloomPersistenceProvider.Memory)
        {
            services.TryAddSingleton<IAsterloomDatabaseMigrator, InMemoryDatabaseMigrator>();
            services.TryAddSingleton<IOutboxStore, InMemoryOutboxStore>();
            services.TryAddSingleton<IAuditStore, InMemoryAuditStore>();
            services.TryAddSingleton<IAuthorizationStore, InMemoryAuthorizationStore>();
            services.TryAddSingleton<IPlatformResourceStore, InMemoryPlatformResourceStore>();
            services.TryAddSingleton<ITargetingStore, InMemoryTargetingStore>();
            services.TryAddSingleton<IFeatureStore, InMemoryFeatureStore>();
            services.TryAddSingleton<IMailStore, InMemoryMailStore>();
            services.TryAddSingleton<IConfigStore, InMemoryConfigStore>();
            services.TryAddSingleton<IStorageStore, InMemoryStorageStore>();
            services.TryAddSingleton<IReleaseStore, InMemoryReleaseStore>();
            services.TryAddSingleton<IAnalyticsStore, InMemoryAnalyticsStore>();
            services.TryAddSingleton<ITelemetryStore, InMemoryTelemetryStore>();
            services.TryAddSingleton<IObjectStorageTransport, InMemoryObjectStorageTransport>();
            services.TryAddSingleton<IMailTransport, MailKitTransport>();
            return;
        }

        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(options.ConnectionString!);
            return builder.Build();
        });
        services.TryAddSingleton<IAsterloomDatabaseMigrator, PostgreSqlDatabaseMigrator>();
        services.TryAddSingleton<IOutboxStore, PostgreSqlOutboxStore>();
        services.TryAddSingleton<IAuditStore, PostgreSqlAuditStore>();
        services.TryAddSingleton<IAuthorizationStore, PostgreSqlAuthorizationStore>();
        services.TryAddSingleton<IPlatformResourceStore, PostgreSqlPlatformResourceStore>();
        services.TryAddSingleton<ITargetingStore, PostgreSqlTargetingStore>();
        services.TryAddSingleton<IFeatureStore, PostgreSqlFeatureStore>();
        services.TryAddSingleton<IMailStore, PostgreSqlMailStore>();
        services.TryAddSingleton<IConfigStore, PostgreSqlConfigStore>();
        services.TryAddSingleton<IStorageStore, PostgreSqlStorageStore>();
        services.TryAddSingleton<IReleaseStore, PostgreSqlReleaseStore>();
        services.TryAddSingleton<IAnalyticsStore, PostgreSqlAnalyticsStore>();
        services.TryAddSingleton<ITelemetryStore, PostgreSqlTelemetryStore>();
        services.TryAddSingleton<IObjectStorageTransport, S3ObjectStorageTransport>();
        services.TryAddSingleton<IMailTransport, MailKitTransport>();
        services
            .AddHealthChecks()
            .AddCheck<PostgreSqlHealthCheck>(
                "postgresql",
                tags: ["ready", "startup"]);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
