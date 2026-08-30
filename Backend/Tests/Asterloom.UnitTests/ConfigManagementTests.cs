using Asterloom.Modules.Config;
using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Targeting;
using Asterloom.Targeting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class ConfigManagementTests
{
    [Fact]
    public async Task DraftPublishSnapshotEtagRollbackAndStatusLifecycleAreComplete()
    {
        await using var provider = CreateProvider();
        await using var serviceScope = provider.CreateAsyncScope();
        var (tenant, application, environment) = await CreatePlatformScopeAsync(serviceScope);
        var targeting = serviceScope.ServiceProvider
            .GetRequiredService<TargetingManagementService>();
        var management = serviceScope.ServiceProvider
            .GetRequiredService<ConfigManagementService>();
        var runtime = serviceScope.ServiceProvider.GetRequiredService<ConfigRuntimeService>();
        var segment = await targeting.CreateSegmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "cn-users",
            "CN users",
            null,
            RegionRule("cn"),
            CancellationToken.None);
        var definition = StringDefinition(segment.Id, "stable", "preview");
        var entry = await management.CreateEntryAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "ui.banner",
            "UI banner",
            "Dynamic banner copy.",
            ConfigValueKind.Text,
            ConfigVisibility.Client,
            definition,
            CancellationToken.None);

        var validation = await management.ValidateDraftAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            CancellationToken.None);
        Assert.True(validation.Valid);
        var initialDiff = await management.DiffDraftAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            CancellationToken.None);
        Assert.True(initialDiff.Changed);

        entry = await management.PublishAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            entry.Version,
            "config-publish-one",
            CancellationToken.None);
        Assert.Equal(1, entry.PublishedRevision);
        Assert.Equal(1, entry.PublishedSnapshotVersion);

        var context = new TargetingEvaluationContext(
            "unit-config-user",
            application.Id,
            environment.Id,
            region: "CN");
        var clientSnapshot = await runtime.GetSnapshotAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            context,
            ifNoneMatch: null,
            includeServerValues: false,
            CancellationToken.None);
        Assert.Equal("preview", Assert.Single(clientSnapshot.Values).Value.StringValue);
        var notModified = await runtime.GetSnapshotAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            context,
            clientSnapshot.ETag,
            includeServerValues: false,
            CancellationToken.None);
        Assert.True(notModified.NotModified);
        Assert.Empty(notModified.Values);

        var serverEntry = await management.CreateEntryAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "internal.batch-size",
            "Internal batch size",
            null,
            ConfigValueKind.WholeNumber,
            ConfigVisibility.Server,
            new ConfigDefinition(
                "{\"type\":\"integer\",\"minimum\":1}",
                ConfigValue.From(25L),
                []),
            CancellationToken.None);
        serverEntry = await management.PublishAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            serverEntry.Id.ToString(),
            serverEntry.Version,
            "config-publish-server",
            CancellationToken.None);
        clientSnapshot = await runtime.GetSnapshotAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            context,
            null,
            includeServerValues: false,
            CancellationToken.None);
        Assert.DoesNotContain(clientSnapshot.Values, value => value.Key == serverEntry.Key);
        var serverSnapshot = await runtime.GetSnapshotAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            context,
            null,
            includeServerValues: true,
            CancellationToken.None);
        Assert.Contains(serverSnapshot.Values, value => value.Key == serverEntry.Key);

        entry = await management.UpdateDraftAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            entry.DisplayName,
            entry.Description,
            entry.Visibility,
            StringDefinition(segment.Id, "second", "second-preview"),
            entry.Version,
            CancellationToken.None);
        entry = await management.PublishAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            entry.Version,
            "config-publish-two",
            CancellationToken.None);
        entry = await management.RollbackAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            targetRevision: 1,
            entry.Version,
            "config-rollback",
            CancellationToken.None);
        Assert.Equal(3, entry.PublishedRevision);
        Assert.Equal("stable", entry.PublishedDefinition!.DefaultValue.StringValue);

        entry = await management.ArchiveAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            entry.Version,
            "config-archive",
            CancellationToken.None);
        var archivedSnapshot = await runtime.GetSnapshotAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            context,
            null,
            includeServerValues: true,
            CancellationToken.None);
        Assert.DoesNotContain(archivedSnapshot.Values, value => value.Key == entry.Key);
        entry = await management.RestoreAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            entry.Version,
            "config-restore",
            CancellationToken.None);
        Assert.Equal(ConfigResourceStatus.Active, entry.Status);

        var revisions = await management.ListRevisionsAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            20,
            null,
            CancellationToken.None);
        Assert.Equal(3, revisions.Items.Count);
        var snapshots = await management.ListSnapshotsAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            20,
            null,
            CancellationToken.None);
        Assert.Equal(6, snapshots.Items.Count);
    }

    [Fact]
    public async Task ValidationRejectsClientSecretsWrongTypesAndUnavailableSegments()
    {
        await using var provider = CreateProvider();
        await using var serviceScope = provider.CreateAsyncScope();
        var (tenant, application, environment) = await CreatePlatformScopeAsync(serviceScope);
        var management = serviceScope.ServiceProvider
            .GetRequiredService<ConfigManagementService>();
        var entry = await management.CreateEntryAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "service.api-key",
            "API key",
            null,
            ConfigValueKind.Text,
            ConfigVisibility.Client,
            new ConfigDefinition(
                "{\"type\":\"string\"}",
                ConfigValue.From(10L),
                [
                    new ConfigTargetingRule(
                        "missing",
                        Guid.CreateVersion7(),
                        ConfigValue.From("secret")),
                ]),
            CancellationToken.None);
        var validation = await management.ValidateDraftAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            entry.Id.ToString(),
            CancellationToken.None);
        Assert.False(validation.Valid);
        Assert.Contains(validation.Issues, issue => issue.Code == "client_secret_like_key");
        Assert.Contains(validation.Issues, issue => issue.Code == "default_type_mismatch");
        Assert.Contains(validation.Issues, issue => issue.Code == "targeting_segment_unavailable");
        var exception = await Assert.ThrowsAsync<AsterloomException>(() =>
            management.PublishAsync(
                tenant.Id.ToString(),
                application.Id.ToString(),
                environment.Id.ToString(),
                entry.Id.ToString(),
                entry.Version,
                "invalid-config-publish",
                CancellationToken.None));
        Assert.Equal(AsterloomErrorKind.FailedPrecondition, exception.Kind);
    }

    private static ConfigDefinition StringDefinition(
        Guid segmentId,
        string defaultValue,
        string targetedValue) =>
        new(
            "{\"type\":\"string\",\"minLength\":1,\"maxLength\":100}",
            ConfigValue.From(defaultValue),
            [new ConfigTargetingRule("cn-preview", segmentId, ConfigValue.From(targetedValue))]);

    private static TargetingRule RegionRule(string region) => new(
        TargetingMatchMode.All,
        [
            new TargetingCondition(
                "region",
                "region",
                TargetingValueKind.Text,
                TargetingOperator.Equals,
                [TargetingValue.From(region)]),
        ]);

    private static async Task<(
        PlatformTenant Tenant,
        PlatformApplication Application,
        PlatformEnvironment Environment)> CreatePlatformScopeAsync(
        AsyncServiceScope serviceScope)
    {
        var platform = serviceScope.ServiceProvider
            .GetRequiredService<PlatformManagementService>();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await platform.CreateTenantAsync(
            $"config-{suffix}",
            "Config Team",
            CancellationToken.None);
        var application = await platform.CreateApplicationAsync(
            tenant.Id.ToString(),
            $"config-{suffix}",
            "Config App",
            CancellationToken.None);
        var environment = await platform.CreateEnvironmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            "production",
            "Production",
            PlatformEnvironmentType.Production,
            isProtected: false,
            CancellationToken.None);
        return (tenant, application, environment);
    }

    private static ServiceProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "Memory",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAsterloomModules(
            configuration,
            new PlatformModule(),
            new TargetingModule(),
            new ConfigModule(),
            new InfrastructureModule());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
