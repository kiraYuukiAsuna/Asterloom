using Asterloom.Modules.Errors;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Targeting;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Targeting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class TargetingManagementTests
{
    [Fact]
    public async Task SegmentLifecycleSimulationAndConcurrencyAreComplete()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformManagementService>();
        var targeting = scope.ServiceProvider.GetRequiredService<TargetingManagementService>();
        var tenant = await platform.CreateTenantAsync(
            "targeting-team",
            "Targeting Team",
            CancellationToken.None);
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
            isProtected: false,
            CancellationToken.None);
        var rule = new TargetingRule(
            TargetingMatchMode.All,
            [
                new TargetingCondition(
                    "version",
                    "clientVersion",
                    TargetingValueKind.Text,
                    TargetingOperator.SemanticVersionGreaterThan,
                    [TargetingValue.From("2.0.0")]),
                new TargetingCondition(
                    "region",
                    "region",
                    TargetingValueKind.Text,
                    TargetingOperator.Equals,
                    [TargetingValue.From("cn")]),
            ]);

        var segment = await targeting.CreateSegmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "Early.Access",
            "Early access",
            "Contract segment",
            rule,
            CancellationToken.None);

        Assert.Equal("early.access", segment.Key);
        var duplicate = await Assert.ThrowsAsync<AsterloomException>(() =>
            targeting.CreateSegmentAsync(
                tenant.Id.ToString(),
                application.Id.ToString(),
                environment.Id.ToString(),
                "early.access",
                "Duplicate",
                string.Empty,
                rule,
                CancellationToken.None));
        Assert.Equal(AsterloomErrorKind.AlreadyExists, duplicate.Kind);

        var context = new TargetingEvaluationContext(
            "device-42",
            application.Id,
            environment.Id,
            clientVersion: "2.1.0",
            region: "CN");
        var simulation = await targeting.SimulateAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            segment.Id.ToString(),
            context,
            new TargetingBucketPreviewRequest(
                "feature",
                "new-home",
                "stable-salt",
                [new TargetingBucketAllocation("enabled", 0, 100_000)]),
            CancellationToken.None);

        Assert.True(simulation.Matched);
        Assert.True(simulation.BucketEvaluated);
        Assert.Equal("enabled", simulation.SelectedVariant);
        Assert.Equal(2, simulation.ConditionTraces.Count);

        segment = await targeting.UpdateSegmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            segment.Id.ToString(),
            "Early access users",
            segment.Description,
            segment.Rule,
            segment.Version,
            CancellationToken.None);
        var stale = await Assert.ThrowsAsync<AsterloomException>(() =>
            targeting.UpdateSegmentAsync(
                tenant.Id.ToString(),
                application.Id.ToString(),
                environment.Id.ToString(),
                segment.Id.ToString(),
                "Stale",
                segment.Description,
                segment.Rule,
                expectedVersion: 1,
                CancellationToken.None));
        Assert.Equal(AsterloomErrorKind.Conflict, stale.Kind);

        segment = await targeting.ArchiveSegmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            segment.Id.ToString(),
            segment.Version,
            CancellationToken.None);
        var activeOnly = await targeting.ListSegmentsAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            20,
            null,
            null,
            includeArchived: false,
            CancellationToken.None);
        var withArchived = await targeting.ListSegmentsAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            20,
            null,
            "early",
            includeArchived: true,
            CancellationToken.None);
        Assert.Empty(activeOnly.Items);
        Assert.Single(withArchived.Items);

        segment = await targeting.RestoreSegmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            segment.Id.ToString(),
            segment.Version,
            CancellationToken.None);
        Assert.Equal(TargetingResourceStatus.Active, segment.Status);
        Assert.Equal(
            segment,
            await targeting.GetSegmentAsync(
                tenant.Id.ToString(),
                application.Id.ToString(),
                environment.Id.ToString(),
                segment.Id.ToString(),
                CancellationToken.None));
    }

    [Fact]
    public void CatalogDescribesEveryStableOperatorAndBuiltInAttribute()
    {
        var catalog = TargetingManagementService.GetCatalog();

        Assert.Equal(8, catalog.Attributes.Count);
        Assert.Equal(16, catalog.Operators.Count);
        Assert.Equal("v1", catalog.BucketingVersion);
        Assert.Equal(TargetingContract.BucketCount, catalog.BucketCount);
        Assert.Contains(catalog.Attributes, attribute =>
            attribute.Key == "targetingKey" && attribute.Required);
    }

    private static ServiceProvider CreateProvider()
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
            new TargetingModule(),
            new InfrastructureModule());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
