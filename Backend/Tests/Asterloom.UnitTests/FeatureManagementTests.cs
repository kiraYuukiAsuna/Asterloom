using Asterloom.Modules.Errors;
using Asterloom.Modules.Feature;
using Asterloom.Modules.Feature.Model;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Targeting;
using Asterloom.Targeting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class FeatureManagementTests
{
    [Fact]
    public async Task FlagDraftPublishEvaluationRollbackAndStatusLifecycleAreComplete()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (tenant, application, environment) = await CreatePlatformScopeAsync(scope);
        var targeting = scope.ServiceProvider.GetRequiredService<TargetingManagementService>();
        var feature = scope.ServiceProvider.GetRequiredService<FeatureManagementService>();
        var evaluator = scope.ServiceProvider.GetRequiredService<FeatureEvaluationService>();
        var segment = await targeting.CreateSegmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "early-access",
            "Early access",
            "Users running the supported client in CN.",
            new TargetingRule(
                TargetingMatchMode.All,
                [
                    new TargetingCondition(
                        "client-version",
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
                ]),
            CancellationToken.None);
        var firstDefinition = CreateBooleanDefinition(segment.Id, enabled: true);

        var flag = await feature.CreateFlagAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "new-home",
            "New home",
            "Feature lifecycle test.",
            FeatureValueKind.Truth,
            firstDefinition,
            CancellationToken.None);

        Assert.Equal(1, flag.DraftRevision);
        var validation = await feature.ValidateDraftAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            CancellationToken.None);
        Assert.True(validation.Valid);
        Assert.NotEmpty(validation.DefinitionHash);

        flag = await feature.PublishAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            flag.Version,
            "publish-one",
            CancellationToken.None);
        Assert.Equal(1, flag.PublishedRevision);

        var matchingContext = new TargetingEvaluationContext(
            "device-42",
            application.Id,
            environment.Id,
            clientVersion: "2.1.0",
            region: "CN");
        var published = await evaluator.EvaluateAsync(
            new FeatureEvaluationRequest(
                new FeatureScope(tenant.Id, application.Id, environment.Id),
                flag.Key,
                FeatureValueKind.Truth,
                matchingContext),
            CancellationToken.None);
        Assert.Equal("on", published.VariantKey);
        Assert.Equal(FeatureEvaluationReason.TargetingMatch, published.Reason);
        Assert.True(published.Value.BooleanValue);

        var versionBeforeUpdate = flag.Version;
        flag = await feature.UpdateDraftAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            "New home disabled",
            flag.Description,
            CreateBooleanDefinition(segment.Id, enabled: false),
            flag.Version,
            CancellationToken.None);
        var stale = await Assert.ThrowsAsync<AsterloomException>(() =>
            feature.UpdateDraftAsync(
                tenant.Id.ToString(),
                application.Id.ToString(),
                environment.Id.ToString(),
                flag.Id.ToString(),
                "Stale update",
                flag.Description,
                flag.DraftDefinition,
                versionBeforeUpdate,
                CancellationToken.None));
        Assert.Equal(AsterloomErrorKind.Conflict, stale.Kind);

        flag = await feature.PublishAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            flag.Version,
            "publish-two",
            CancellationToken.None);
        var disabled = await feature.SimulateAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            useDraft: false,
            matchingContext,
            CancellationToken.None);
        Assert.Equal(FeatureEvaluationReason.Disabled, disabled.Reason);
        Assert.Equal("off", disabled.VariantKey);

        flag = await feature.RollbackAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            targetRevision: 1,
            flag.Version,
            "rollback-one",
            CancellationToken.None);
        Assert.Equal(3, flag.PublishedRevision);
        Assert.True(flag.PublishedDefinition!.Enabled);
        var revisions = await feature.ListRevisionsAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            pageSize: 20,
            pageToken: null,
            CancellationToken.None);
        Assert.Equal(3, revisions.Items.Count);
        Assert.Equal(1, revisions.Items.Single(item => item.Revision == 3).SourceRevision);

        flag = await feature.ArchiveAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            flag.Version,
            CancellationToken.None);
        var activeOnly = await feature.ListFlagsAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            20,
            null,
            "new-home",
            includeArchived: false,
            CancellationToken.None);
        Assert.Empty(activeOnly.Items);

        flag = await feature.RestoreAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            flag.Version,
            CancellationToken.None);
        Assert.Equal(FeatureResourceStatus.Active, flag.Status);
        Assert.Equal(
            flag,
            await feature.GetFlagAsync(
                tenant.Id.ToString(),
                application.Id.ToString(),
                environment.Id.ToString(),
                flag.Id.ToString(),
                CancellationToken.None));
    }

    [Fact]
    public async Task ValidationBlocksWrongTypesAndUnavailableTargetingSegments()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (tenant, application, environment) = await CreatePlatformScopeAsync(scope);
        var feature = scope.ServiceProvider.GetRequiredService<FeatureManagementService>();
        var definition = new FeatureDefinition(
            Enabled: true,
            DefaultVariantKey: "bad",
            Variants: [new FeatureVariant("bad", "Bad", FeatureValue.From("wrong type"))],
            Prerequisites: [],
            TargetingRules:
            [
                new FeatureTargetingRule("missing-segment", Guid.CreateVersion7(), "bad"),
            ],
            Allocations: [],
            BucketingSalt: "validation-salt");
        var flag = await feature.CreateFlagAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "invalid-flag",
            "Invalid flag",
            null,
            FeatureValueKind.Truth,
            definition,
            CancellationToken.None);

        var validation = await feature.ValidateDraftAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            CancellationToken.None);

        Assert.False(validation.Valid);
        Assert.Contains(validation.Issues, issue => issue.Code == "variant_type_mismatch");
        Assert.Contains(validation.Issues, issue => issue.Code == "targeting_segment_unavailable");
        var publish = await Assert.ThrowsAsync<AsterloomException>(() => feature.PublishAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            flag.Version,
            "invalid-publish",
            CancellationToken.None));
        Assert.Equal(AsterloomErrorKind.FailedPrecondition, publish.Kind);
    }

    private static FeatureDefinition CreateBooleanDefinition(Guid segmentId, bool enabled) => new(
        enabled,
        "off",
        [
            new FeatureVariant("off", "Off", FeatureValue.From(false)),
            new FeatureVariant("on", "On", FeatureValue.From(true)),
        ],
        [],
        [new FeatureTargetingRule("early-access", segmentId, "on")],
        [
            new FeatureAllocation("off", 0, 50_000),
            new FeatureAllocation("on", 50_000, 100_000),
        ],
        "stable-test-salt");

    private static async Task<(
        PlatformTenant Tenant,
        PlatformApplication Application,
        PlatformEnvironment Environment)> CreatePlatformScopeAsync(
        AsyncServiceScope scope)
    {
        var platform = scope.ServiceProvider.GetRequiredService<PlatformManagementService>();
        var tenant = await platform.CreateTenantAsync(
            "feature-team",
            "Feature Team",
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
        return (tenant, application, environment);
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
            new FeatureModule(),
            new InfrastructureModule());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
