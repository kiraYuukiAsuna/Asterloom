using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Requests;
using Asterloom.Targeting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class AuthorizationTests
{
    [Fact]
    public void SystemPermissionsHaveStableUniqueIdentifiers()
    {
        var firstSnapshot = AuthorizationCatalog.Permissions
            .ToDictionary(static permission => permission.Key, static permission => permission.Id);
        var secondSnapshot = AuthorizationCatalog.Permissions
            .ToDictionary(static permission => permission.Key, static permission => permission.Id);

        Assert.DoesNotContain(Guid.Empty, firstSnapshot.Values);
        Assert.Equal(firstSnapshot.Count, firstSnapshot.Values.Distinct().Count());
        Assert.Equal(firstSnapshot, secondSnapshot);
    }

    [Fact]
    public async Task DecisionEngineDefaultsToDenyAndHonorsTrustedPassportRole()
    {
        await using var provider = CreateProvider();
        var decisionService = provider.GetRequiredService<AuthorizationDecisionService>();
        var denied = await decisionService.DecideAsync(
            new AuthorizationDecisionRequest(
                "actor-1",
                AuthorizationScope.Global,
                "platform.tenant.read",
                []),
            CancellationToken.None);
        var allowed = await decisionService.DecideAsync(
            new AuthorizationDecisionRequest(
                "actor-1",
                AuthorizationScope.Global,
                "platform.tenant.create",
                ["SuperAdministrator"]),
            CancellationToken.None);

        Assert.False(denied.Allowed);
        Assert.True(allowed.Allowed);
        Assert.Contains("super-administrator", allowed.MatchedRoleKeys);
    }

    [Fact]
    public async Task ScopedRoleAllowsOnlyItsScopeAndExplicitDenyOverridesIt()
    {
        await using var provider = CreateProvider();
        await using var serviceScope = provider.CreateAsyncScope();
        var management = serviceScope.ServiceProvider
            .GetRequiredService<AuthorizationManagementService>();
        var applicationScope = await CreateApplicationScopeAsync(serviceScope.ServiceProvider);
        var tenantId = applicationScope.TenantId!.Value;
        var applicationId = applicationScope.ApplicationId!.Value;
        var environmentId = Guid.CreateVersion7();
        var actorId = "client:desktop-agent";
        var role = await management.CreateRoleAsync(
            "release-operator",
            "Release Operator",
            "Can update environments in an assigned application.",
            ["platform.environment.update"],
            applicationScope,
            CancellationToken.None);
        await management.SetRoleBindingAsync(
            Guid.CreateVersion7().ToString("D"),
            actorId,
            role.Id.ToString("D"),
            new AuthorizationScope(tenantId, applicationId, null),
            expectedVersion: 0,
            CancellationToken.None);

        var inside = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                new AuthorizationScope(tenantId, applicationId, environmentId),
                "platform.environment.update",
                []),
            CancellationToken.None);
        var outside = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                new AuthorizationScope(Guid.CreateVersion7(), applicationId, environmentId),
                "platform.environment.update",
                []),
            CancellationToken.None);

        Assert.True(inside.Allowed);
        Assert.False(outside.Allowed);
        Assert.Empty(outside.MatchedRoleKeys);

        await management.CreatePolicyRuleAsync(
            "Freeze releases",
            AuthorizationPolicyEffect.Deny,
            AuthorizationPolicySubjectType.Actor,
            actorId,
            new AuthorizationScope(tenantId, applicationId, null),
            "platform.environment.update",
            string.Empty,
            string.Empty,
            condition: null,
            CancellationToken.None);
        var explicitlyDenied = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                new AuthorizationScope(tenantId, applicationId, environmentId),
                "platform.environment.update",
                []),
            CancellationToken.None);
        var revisions = await management.ListPolicyRevisionsAsync(
            pageSize: 20,
            pageToken: null,
            resourceType: null,
            resourceId: null,
            CancellationToken.None);

        Assert.False(explicitlyDenied.Allowed);
        Assert.Contains("explicit deny", explicitlyDenied.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, revisions.Items.Count);
    }

    [Fact]
    public async Task RolePagingContinuesFromSystemRolesIntoCustomRoles()
    {
        await using var provider = CreateProvider();
        await using var serviceScope = provider.CreateAsyncScope();
        var management = serviceScope.ServiceProvider
            .GetRequiredService<AuthorizationManagementService>();
        var applicationScope = await CreateApplicationScopeAsync(serviceScope.ServiceProvider);
        await management.CreateRoleAsync(
            "custom-reader",
            "Custom Reader",
            string.Empty,
            ["platform.tenant.read"],
            applicationScope,
            CancellationToken.None);

        var roles = new List<AuthorizationRole>();
        string? token = null;
        do
        {
            var page = await management.ListRolesAsync(
                pageSize: 1,
                token,
                query: null,
                includeArchived: false,
                tenantId: applicationScope.TenantId!.Value.ToString("D"),
                applicationId: applicationScope.ApplicationId!.Value.ToString("D"),
                CancellationToken.None);
            roles.AddRange(page.Items);
            token = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(token));

        Assert.Equal(AuthorizationCatalog.SystemRoles.Count + 1, roles.Count);
        Assert.Contains(roles, static role => role.Key == "custom-reader");
    }

    [Fact]
    public async Task ApplicationPermissionsRbacAclAndAbacAreIsolatedAndComposable()
    {
        await using var provider = CreateProvider();
        await using var serviceScope = provider.CreateAsyncScope();
        var management = serviceScope.ServiceProvider
            .GetRequiredService<AuthorizationManagementService>();
        var applicationScope = await CreateApplicationScopeAsync(serviceScope.ServiceProvider);
        var actorId = Guid.CreateVersion7().ToString("D");
        var readPermission = await management.CreatePermissionAsync(
            applicationScope,
            "orders.read",
            "Read orders",
            "Reads business orders.",
            CancellationToken.None);
        await management.CreatePermissionAsync(
            applicationScope,
            "orders.refund",
            "Refund orders",
            "Refunds one business order.",
            CancellationToken.None);
        var role = await management.CreateRoleAsync(
            "order-reader",
            "Order reader",
            "Reads orders in this application.",
            ["orders.read"],
            applicationScope,
            CancellationToken.None);
        await management.SetRoleBindingAsync(
            Guid.CreateVersion7().ToString("D"),
            actorId,
            role.Id.ToString("D"),
            applicationScope,
            expectedVersion: 0,
            CancellationToken.None);

        var rbac = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                applicationScope,
                "orders.read",
                []),
            CancellationToken.None);
        Assert.True(rbac.Allowed);

        var otherApplicationScope = await CreateApplicationScopeAsync(
            serviceScope.ServiceProvider);
        await management.CreatePermissionAsync(
            otherApplicationScope,
            "orders.read",
            "Read orders",
            "A separate application may reuse the same permission key.",
            CancellationToken.None);
        var crossApplication = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                otherApplicationScope,
                "orders.read",
                []),
            CancellationToken.None);
        Assert.False(crossApplication.Allowed);

        await Assert.ThrowsAsync<Asterloom.Modules.Errors.AsterloomException>(() =>
            management.CreateRoleAsync(
                "invalid-refunder",
                "Invalid refunder",
                "Cannot import a permission from another application.",
                ["orders.refund"],
                otherApplicationScope,
                CancellationToken.None));
        await Assert.ThrowsAsync<Asterloom.Modules.Errors.AsterloomException>(() =>
            management.CreatePermissionAsync(
                applicationScope,
                "platform.custom-action",
                "Reserved permission",
                string.Empty,
                CancellationToken.None));

        await management.CreatePolicyRuleAsync(
            "Refund selected order",
            AuthorizationPolicyEffect.Allow,
            AuthorizationPolicySubjectType.Actor,
            actorId,
            applicationScope,
            "orders.refund",
            "order",
            "order-42",
            condition: null,
            CancellationToken.None);
        var aclAllowed = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                applicationScope,
                "orders.refund",
                [],
                "order",
                "order-42"),
            CancellationToken.None);
        var aclDenied = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                applicationScope,
                "orders.refund",
                [],
                "order",
                "order-99"),
            CancellationToken.None);
        Assert.True(aclAllowed.Allowed);
        Assert.False(aclDenied.Allowed);

        var financeCondition = new TargetingRule(
            TargetingMatchMode.All,
            [
                new TargetingCondition(
                    "finance-department",
                    "subject.department",
                    TargetingValueKind.Text,
                    TargetingOperator.Equals,
                    [TargetingValue.From("finance")]),
            ]);
        await management.CreatePolicyRuleAsync(
            "Finance refunds",
            AuthorizationPolicyEffect.Allow,
            AuthorizationPolicySubjectType.Any,
            "*",
            applicationScope,
            "orders.refund",
            "order",
            string.Empty,
            financeCondition,
            CancellationToken.None);
        var abacAllowed = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                applicationScope,
                "orders.refund",
                [],
                "order",
                "order-99",
                new Dictionary<string, TargetingValue>
                {
                    ["subject.department"] = TargetingValue.From("finance"),
                }),
            CancellationToken.None);
        var abacDenied = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                applicationScope,
                "orders.refund",
                [],
                "order",
                "order-99",
                new Dictionary<string, TargetingValue>
                {
                    ["subject.department"] = TargetingValue.From("sales"),
                }),
            CancellationToken.None);
        Assert.True(abacAllowed.Allowed);
        Assert.False(abacDenied.Allowed);

        await management.ArchivePermissionAsync(
            readPermission.Id.ToString("D"),
            readPermission.Version,
            CancellationToken.None);
        var archived = await management.SimulateAsync(
            new AuthorizationDecisionRequest(
                actorId,
                applicationScope,
                "orders.read",
                []),
            CancellationToken.None);
        Assert.False(archived.Allowed);
    }

    private static async Task<AuthorizationScope> CreateApplicationScopeAsync(
        IServiceProvider serviceProvider)
    {
        var platform = serviceProvider.GetRequiredService<PlatformManagementService>();
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = await platform.CreateTenantAsync(
            "authorization-" + suffix,
            "Authorization tenant",
            CancellationToken.None);
        var application = await platform.CreateApplicationAsync(
            tenant.Id.ToString("D"),
            "application-" + suffix,
            "Authorization application",
            CancellationToken.None);
        return new AuthorizationScope(tenant.Id, application.Id, null);
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
        services.AddSingleton<IAsterloomRequestContextAccessor>(
            new TestRequestContextAccessor());
        services.AddAsterloomModules(
            configuration,
            new PlatformModule(),
            new AuthorizationModule(),
            new InfrastructureModule());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class TestRequestContextAccessor : IAsterloomRequestContextAccessor
    {
        public AsterloomRequestContext Current { get; } = new(
            "test-request",
            "test-administrator",
            null,
            null,
            null);
    }
}
