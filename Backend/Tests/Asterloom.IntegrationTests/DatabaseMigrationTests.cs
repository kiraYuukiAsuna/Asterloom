using System.Security.Cryptography;
using Asterloom.Modules.Analytics;
using Asterloom.Modules.Auditing;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Config;
using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Feature;
using Asterloom.Modules.Feature.Model;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Identity.Bootstrap;
using Asterloom.Modules.Identity.Persistence;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Infrastructure.Persistence;
using Asterloom.Modules.Mail;
using Asterloom.Modules.Mail.Model;
using Asterloom.Modules.Outbox;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Release;
using Asterloom.Modules.Release.Model;
using Asterloom.Modules.Requests;
using Asterloom.Modules.Storage;
using Asterloom.Modules.Targeting;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Telemetry;
using Asterloom.Targeting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Asterloom.IntegrationTests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task PostgreSqlMigrationsAreTransactionalAndIdempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ASTERLOOM_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "PostgreSql",
                    ["ConnectionStrings:Asterloom"] = connectionString,
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAsterloomRequestContextAccessor>(
            new TestRequestContextAccessor());
        services.AddAsterloomModules(
            configuration,
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
            new MailModule(),
            new InfrastructureModule());
        services.AddAsterloomIdentityCore(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var migrator = provider.GetRequiredService<IAsterloomDatabaseMigrator>();

        var firstRun = await migrator.MigrateAsync(CancellationToken.None);
        var secondRun = await migrator.MigrateAsync(CancellationToken.None);
        using var identityScope = provider.CreateScope();
        var identityMigrator = identityScope.ServiceProvider
            .GetRequiredService<IIdentityDatabaseMigrator>();
        await identityMigrator.MigrateAsync(CancellationToken.None);
        await identityMigrator.MigrateAsync(CancellationToken.None);
        var identityBootstrapper = identityScope.ServiceProvider
            .GetRequiredService<IIdentityBootstrapper>();
        await identityBootstrapper.BootstrapAsync(CancellationToken.None);
        await identityBootstrapper.BootstrapAsync(CancellationToken.None);
        var platform = identityScope.ServiceProvider
            .GetRequiredService<PlatformManagementService>();
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = await platform.CreateTenantAsync(
            "tenant-" + suffix,
            "Migration Test",
            CancellationToken.None);
        var application = await platform.CreateApplicationAsync(
            tenant.Id.ToString(),
            "app-" + suffix,
            "Migration App",
            CancellationToken.None);
        var environment = await platform.CreateEnvironmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            "development",
            "Development",
            PlatformEnvironmentType.Development,
            isProtected: false,
            CancellationToken.None);
        var persistedEnvironments = await platform.ListEnvironmentsAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            pageSize: 20,
            pageToken: null,
            query: "develop",
            includeArchived: false,
            CancellationToken.None);
        var mail = identityScope.ServiceProvider
            .GetRequiredService<MailAccountManagementService>();
        var smtpAccount = await mail.CreateAccountAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            "migration-smtp-" + suffix,
            "smtp.example.com",
            465,
            SmtpSecurityMode.SslOnConnect,
            "mailer@example.com",
            "migration-authorization-code",
            "mailer@example.com",
            "Migration Mail",
            CancellationToken.None);
        var persistedSmtpAccounts = await mail.ListAccountsAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            pageSize: 20,
            pageToken: null,
            query: "migration-smtp",
            includeArchived: false,
            CancellationToken.None);
        var targeting = identityScope.ServiceProvider
            .GetRequiredService<TargetingManagementService>();
        var segment = await targeting.CreateSegmentAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "migration-segment-" + suffix,
            "Migration segment",
            "Verifies the PostgreSQL targeting adapter.",
            new TargetingRule(
                TargetingMatchMode.All,
                [
                    new TargetingCondition(
                        "region",
                        "region",
                        TargetingValueKind.Text,
                        TargetingOperator.Equals,
                        [TargetingValue.From("cn")]),
                ]),
            CancellationToken.None);
        var targetingSimulation = await targeting.SimulateAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            segment.Id.ToString(),
            new TargetingEvaluationContext(
                "migration-user",
                application.Id,
                environment.Id,
                region: "CN"),
            new TargetingBucketPreviewRequest(
                "feature",
                "migration-preview",
                "v1",
                [new TargetingBucketAllocation("enabled", 0, 100_000)]),
            CancellationToken.None);
        var feature = identityScope.ServiceProvider
            .GetRequiredService<FeatureManagementService>();
        var flag = await feature.CreateFlagAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "migration-flag-" + suffix,
            "Migration feature",
            "Verifies the PostgreSQL feature adapter and transactional publish.",
            FeatureValueKind.Truth,
            new FeatureDefinition(
                Enabled: true,
                DefaultVariantKey: "off",
                Variants:
                [
                    new FeatureVariant("off", "Off", FeatureValue.From(false)),
                    new FeatureVariant("on", "On", FeatureValue.From(true)),
                ],
                Prerequisites: [],
                TargetingRules:
                [
                    new FeatureTargetingRule("migration-segment", segment.Id, "on"),
                ],
                Allocations:
                [
                    new FeatureAllocation("off", 0, 100_000),
                ],
                BucketingSalt: "migration-salt"),
            CancellationToken.None);
        flag = await feature.PublishAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            flag.Version,
            "migration-feature-publish",
            CancellationToken.None);
        var featureEvaluation = await feature.SimulateAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            flag.Id.ToString(),
            useDraft: false,
            new TargetingEvaluationContext(
                "migration-user",
                application.Id,
                environment.Id,
                region: "CN"),
            CancellationToken.None);
        var config = identityScope.ServiceProvider
            .GetRequiredService<ConfigManagementService>();
        var configRuntime = identityScope.ServiceProvider
            .GetRequiredService<ConfigRuntimeService>();
        var configEntry = await config.CreateEntryAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "migration-config-" + suffix,
            "Migration config",
            "Verifies PostgreSQL atomic snapshots and JSONB persistence.",
            ConfigValueKind.Text,
            ConfigVisibility.Client,
            new ConfigDefinition(
                "{\"type\":\"string\"}",
                ConfigValue.From("stable"),
                [
                    new ConfigTargetingRule(
                        "migration-segment",
                        segment.Id,
                        ConfigValue.From("preview")),
                ]),
            CancellationToken.None);
        configEntry = await config.PublishAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            configEntry.Id.ToString(),
            configEntry.Version,
            "migration-config-publish",
            CancellationToken.None);
        var configSnapshot = await configRuntime.GetSnapshotAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            new TargetingEvaluationContext(
                "migration-user",
                application.Id,
                environment.Id,
                region: "CN"),
            ifNoneMatch: null,
            includeServerValues: false,
            CancellationToken.None);
        var releaseManagement = identityScope.ServiceProvider
            .GetRequiredService<ReleaseManagementService>();
        using var releaseKeyMaterial = RSA.Create(2048);
        var releaseSigningKey = await releaseManagement.CreateSigningKeyAsync(
            tenant.Id.ToString(),
            "migration-key-" + suffix,
            "Migration release key",
            releaseKeyMaterial.ExportSubjectPublicKeyInfoPem(),
            CancellationToken.None);
        var releaseChannel = await releaseManagement.CreateChannelAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            "migration-channel-" + suffix,
            "Migration channel",
            "Verifies PostgreSQL release persistence.",
            CancellationToken.None);
        var desktopRelease = await releaseManagement.CreateReleaseAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            releaseChannel.Id.ToString(),
            "1.0.0",
            "Migration release",
            "Draft persistence test.",
            [],
            100_000,
            targetSegmentId: null,
            mandatory: false,
            minimumVersion: "0.9.0",
            CancellationToken.None);
        desktopRelease = await releaseManagement.UpdateReleaseDraftAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            desktopRelease.Id.ToString(),
            "Migration release updated",
            desktopRelease.ReleaseNotes,
            [],
            desktopRelease.RolloutBasisPoints,
            targetSegmentId: null,
            desktopRelease.Mandatory,
            desktopRelease.MinimumVersion,
            desktopRelease.Version,
            CancellationToken.None);
        var releaseValidation = await releaseManagement.ValidateReleaseAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            desktopRelease.Id.ToString(),
            CancellationToken.None);
        var persistedReleases = await releaseManagement.ListReleasesAsync(
            tenant.Id.ToString(),
            application.Id.ToString(),
            environment.Id.ToString(),
            20,
            null,
            "migration",
            includeInactive: true,
            CancellationToken.None);
        var authorization = identityScope.ServiceProvider
            .GetRequiredService<AuthorizationManagementService>();
        var actorId = "migration-test-client-" + suffix;
        var role = await authorization.CreateRoleAsync(
            "migration-role-" + suffix,
            "Migration Role",
            "Verifies the PostgreSQL authorization adapter.",
            ["platform.environment.update"],
            CancellationToken.None);
        await authorization.SetRoleBindingAsync(
            Guid.CreateVersion7().ToString("D"),
            actorId,
            role.Id.ToString("D"),
            new AuthorizationScope(tenant.Id, application.Id, null),
            expectedVersion: 0,
            CancellationToken.None);
        var authorizationDecision = await authorization.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                new AuthorizationScope(tenant.Id, application.Id, environment.Id),
                "platform.environment.update",
                []),
            CancellationToken.None);
        var revisions = await authorization.ListPolicyRevisionsAsync(
            pageSize: 20,
            pageToken: null,
            resourceType: null,
            resourceId: null,
            CancellationToken.None);
        var auditStore = identityScope.ServiceProvider.GetRequiredService<IAuditStore>();
        var auditId = Guid.CreateVersion7();
        await auditStore.AppendAsync(
            new AsterloomAuditEvent(
                auditId,
                actorId,
                tenant.Id,
                application.Id,
                environment.Id,
                "migration-test",
                "environment",
                environment.Id.ToString("D"),
                "migration-request",
                AuditOutcome.Succeeded,
                string.Empty,
                "request_fields=[environment_id]",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var audit = await auditStore.GetAsync(auditId, CancellationToken.None);
        var outboxStore = identityScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var rolledBackEvent = OutboxMessageFactory.Create(
            "asterloom.integration.rollback.v1",
            1,
            new { environmentId = environment.Id },
            "migration-rollback",
            DateTimeOffset.UtcNow,
            tenant.Id,
            application.Id,
            environment.Id);
        await using (var connection = await identityScope.ServiceProvider
                         .GetRequiredService<NpgsqlDataSource>()
                         .OpenConnectionAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await outboxStore.EnqueueAsync(
                rolledBackEvent,
                connection,
                transaction,
                CancellationToken.None);
            await transaction.RollbackAsync();
        }

        var committedEvent = OutboxMessageFactory.Create(
            "asterloom.integration.commit.v1",
            1,
            new { environmentId = environment.Id },
            "migration-commit",
            DateTimeOffset.UtcNow,
            tenant.Id,
            application.Id,
            environment.Id);
        await using (var connection = await identityScope.ServiceProvider
                         .GetRequiredService<NpgsqlDataSource>()
                         .OpenConnectionAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await outboxStore.EnqueueAsync(
                committedEvent,
                connection,
                transaction,
                CancellationToken.None);
            await transaction.CommitAsync();
        }

        Assert.True(firstRun.IsPersistent);
        Assert.Equal(12, firstRun.AppliedCount);
        Assert.Equal(0, secondRun.AppliedCount);
        Assert.Equal(12, secondRun.PreviouslyAppliedCount);
        Assert.Contains(
            persistedEnvironments.Items,
            candidate => candidate.Id == environment.Id);
        Assert.Contains(
            persistedSmtpAccounts.Items,
            candidate => candidate.Id == smtpAccount.Id);
        Assert.True(authorizationDecision.Allowed);
        Assert.True(targetingSimulation.Matched);
        Assert.Equal("enabled", targetingSimulation.SelectedVariant);
        Assert.Equal("on", featureEvaluation.VariantKey);
        Assert.Equal(1, flag.PublishedRevision);
        Assert.Equal(1, configEntry.PublishedRevision);
        Assert.Equal("preview", Assert.Single(configSnapshot.Values).Value.StringValue);
        Assert.False(releaseValidation.Valid);
        Assert.Contains(persistedReleases.Items, item => item.Id == desktopRelease.Id);
        Assert.Equal(ReleaseSigningKeyStatus.Active, releaseSigningKey.Status);
        Assert.Contains(revisions.Items, revision => revision.ResourceId == role.Id.ToString());
        Assert.NotNull(audit);
        Assert.Null(await outboxStore.GetAsync(rolledBackEvent.Id, CancellationToken.None));
        Assert.NotNull(await outboxStore.GetAsync(committedEvent.Id, CancellationToken.None));

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(
            """
            SELECT
                to_regclass('platform.tenants') IS NOT NULL
                AND to_regclass('platform.applications') IS NOT NULL
                AND to_regclass('platform.environments') IS NOT NULL
                AND to_regclass('authorization.roles') IS NOT NULL
                AND to_regclass('authorization.role_bindings') IS NOT NULL
                AND to_regclass('authorization.policy_rules') IS NOT NULL
                AND to_regclass('authorization.policy_revisions') IS NOT NULL
                AND to_regclass('targeting.segments') IS NOT NULL
                AND to_regclass('feature.flags') IS NOT NULL
                AND to_regclass('feature.revisions') IS NOT NULL
                AND to_regclass('config.entries') IS NOT NULL
                AND to_regclass('config.revisions') IS NOT NULL
                AND to_regclass('config.snapshots') IS NOT NULL
                AND to_regclass('storage.buckets') IS NOT NULL
                AND to_regclass('storage.objects') IS NOT NULL
                AND to_regclass('storage.upload_sessions') IS NOT NULL
                AND to_regclass('release.signing_keys') IS NOT NULL
                AND to_regclass('release.channels') IS NOT NULL
                AND to_regclass('release.artifacts') IS NOT NULL
                AND to_regclass('release.releases') IS NOT NULL
                AND to_regclass('mail.smtp_accounts') IS NOT NULL
                AND to_regclass('mail.deliveries') IS NOT NULL
                AND to_regclass('infrastructure.audit_events') IS NOT NULL
                AND to_regclass('infrastructure.outbox_messages') IS NOT NULL
                AND to_regclass('infrastructure.inbox_receipts') IS NOT NULL
                AND to_regclass('identity."AspNetUsers"') IS NOT NULL
                AND to_regclass('identity."OpenIddictApplications"') IS NOT NULL
                AND to_regclass('identity.__ef_migrations_history') IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM identity."OpenIddictScopes"
                    WHERE "Name" = 'asterloom.api'
                )
                AND to_regclass('infrastructure.schema_migrations') IS NOT NULL;
            """);
        var tablesExist = await command.ExecuteScalarAsync();
        Assert.Equal(true, tablesExist);
    }

    private sealed class TestRequestContextAccessor : IAsterloomRequestContextAccessor
    {
        public AsterloomRequestContext Current { get; } = new(
            "migration-test",
            "migration-test-administrator",
            null,
            null,
            null);
    }
}
