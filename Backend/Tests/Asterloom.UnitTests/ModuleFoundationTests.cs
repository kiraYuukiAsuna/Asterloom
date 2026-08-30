using Asterloom.Modules.Hosting;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Infrastructure.Persistence;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Platform.Model;
using Asterloom.Protocol.Platform.Admin.V1;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class ModuleFoundationTests
{
    [Fact]
    public void RegistryRejectsDuplicateModuleNamesIgnoringCase()
    {
        IAsterloomModule[] modules =
        [
            new StubModule("identity"),
            new StubModule("IDENTITY"),
        ];

        var exception = Assert.Throws<InvalidOperationException>(
            () => new AsterloomModuleRegistry(modules));

        Assert.Contains("identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlatformCatalogReportsImplementedVerticalSlices()
    {
        var now = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
        var provider = new PlatformInfoProvider(new FixedTimeProvider(now));

        var response = provider.GetPlatformInfo();

        Assert.Equal(12, response.Capabilities.Count);
        Assert.Equal(
            CapabilityLifecycle.Available,
            response.Capabilities.Single(capability => capability.Key == "rpc").Lifecycle);
        Assert.Equal(
            CapabilityLifecycle.Available,
            response.Capabilities.Single(capability => capability.Key == "web").Lifecycle);
        Assert.Equal(
            CapabilityLifecycle.Available,
            response.Capabilities.Single(capability => capability.Key == "identity").Lifecycle);
        Assert.Equal(
            CapabilityLifecycle.Available,
            response.Capabilities.Single(capability => capability.Key == "authorization").Lifecycle);
        Assert.Equal(
            CapabilityLifecycle.Available,
            response.Capabilities.Single(capability => capability.Key == "targeting").Lifecycle);
        Assert.Equal(
            CapabilityLifecycle.Available,
            response.Capabilities.Single(capability => capability.Key == "feature").Lifecycle);
        Assert.Equal(
            CapabilityLifecycle.Available,
            response.Capabilities.Single(capability => capability.Key == "config").Lifecycle);
        Assert.Equal(
            CapabilityLifecycle.Available,
            response.Capabilities.Single(capability => capability.Key == "persistence").Lifecycle);
        Assert.Equal(now, response.ServerTime.ToDateTimeOffset());
    }

    [Fact]
    public async Task MemoryInfrastructureKeepsExplicitMigrationCatalogWithoutApplyingIt()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "Memory",
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAsterloomModules(
            configuration,
            new PlatformModule(),
            new InfrastructureModule());

        using var provider = services.BuildServiceProvider(validateScopes: true);
        Assert.Same(configuration, provider.GetRequiredService<IConfiguration>());
        var migrations = provider.GetServices<IAsterloomModuleMigration>().ToArray();
        var result = await provider
            .GetRequiredService<IAsterloomDatabaseMigrator>()
            .MigrateAsync(CancellationToken.None);

        Assert.Equal(2, migrations.Length);
        Assert.Contains(
            migrations,
            migration => migration.ModuleName == "platform"
                && migration.Version == 1
                && migration.Sql.Contains("platform.environments", StringComparison.Ordinal));
        Assert.Contains(
            migrations,
            migration => migration.ModuleName == "infrastructure"
                && migration.Version == 1
                && migration.Sql.Contains("outbox_messages", StringComparison.Ordinal));
        Assert.False(result.IsPersistent);
        Assert.Equal(0, result.AppliedCount);
    }

    [Fact]
    public async Task PlatformHierarchyEnforcesScopeConcurrencyProtectionAndMembershipLifecycle()
    {
        var now = new DateTimeOffset(2026, 8, 30, 1, 0, 0, TimeSpan.Zero);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "Memory",
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddAsterloomModules(
            configuration,
            new PlatformModule(),
            new InfrastructureModule());

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformManagementService>();

        var tenant = await platform.CreateTenantAsync(
            "acme-team",
            "Acme Team",
            CancellationToken.None);
        var duplicate = await Assert.ThrowsAsync<AsterloomException>(() =>
            platform.CreateTenantAsync("ACME-TEAM", "Duplicate", CancellationToken.None));
        Assert.Equal(AsterloomErrorKind.AlreadyExists, duplicate.Kind);

        var application = await platform.CreateApplicationAsync(
            tenant.Id.ToString(),
            "desktop-app",
            "Desktop App",
            CancellationToken.None);
        var environment = await platform.CreateEnvironmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            "production",
            "Production",
            PlatformEnvironmentType.Production,
            isProtected: true,
            CancellationToken.None);

        var protectedFailure = await Assert.ThrowsAsync<AsterloomException>(() =>
            platform.ArchiveEnvironmentAsync(
                tenant.Id.ToString(),
                application.Id.ToString(),
                environment.Id.ToString(),
                environment.Version,
                CancellationToken.None));
        Assert.Equal(AsterloomErrorKind.FailedPrecondition, protectedFailure.Kind);

        environment = await platform.UpdateEnvironmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            environment.DisplayName,
            environment.EnvironmentType,
            isProtected: false,
            environment.Version,
            CancellationToken.None);
        var staleWrite = await Assert.ThrowsAsync<AsterloomException>(() =>
            platform.UpdateEnvironmentAsync(
                tenant.Id.ToString(),
                application.Id.ToString(),
                environment.Id.ToString(),
                "Stale",
                environment.EnvironmentType,
                isProtected: false,
                expectedVersion: 1,
                CancellationToken.None));
        Assert.Equal(AsterloomErrorKind.Conflict, staleWrite.Kind);

        environment = await platform.ArchiveEnvironmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            environment.Version,
            CancellationToken.None);
        Assert.Equal(PlatformResourceStatus.Archived, environment.Status);
        environment = await platform.RestoreEnvironmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            environment.Version,
            CancellationToken.None);
        Assert.Equal(PlatformResourceStatus.Active, environment.Status);

        var actorId = Guid.CreateVersion7();
        var membership = await platform.SetTenantMembershipAsync(
            tenant.Id.ToString(),
            actorId.ToString(),
            expectedVersion: 0,
            CancellationToken.None);
        membership = await platform.RemoveTenantMembershipAsync(
            tenant.Id.ToString(),
            actorId.ToString(),
            membership.Version,
            CancellationToken.None);
        membership = await platform.SetTenantMembershipAsync(
            tenant.Id.ToString(),
            actorId.ToString(),
            membership.Version,
            CancellationToken.None);
        Assert.Equal(PlatformMembershipStatus.Active, membership.Status);

        var tenants = await platform.ListTenantsAsync(
            pageSize: 1,
            pageToken: null,
            query: "acme",
            includeArchived: false,
            CancellationToken.None);
        var applications = await platform.ListApplicationsAsync(
            tenant.Id.ToString(),
            pageSize: 20,
            pageToken: null,
            query: null,
            includeArchived: false,
            CancellationToken.None);
        var environments = await platform.ListEnvironmentsAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            pageSize: 20,
            pageToken: null,
            query: null,
            includeArchived: false,
            CancellationToken.None);

        Assert.Single(tenants.Items);
        Assert.Single(applications.Items);
        Assert.Single(environments.Items);
    }

    private sealed class StubModule(string name) : IAsterloomModule
    {
        public string Name { get; } = name;

        public void AddServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
