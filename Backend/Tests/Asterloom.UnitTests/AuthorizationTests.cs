using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Requests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class AuthorizationTests
{
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
        var tenantId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var actorId = "client:desktop-agent";
        var role = await management.CreateRoleAsync(
            "release-operator",
            "Release Operator",
            "Can update environments in an assigned application.",
            ["platform.environment.update"],
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
        await management.CreateRoleAsync(
            "custom-reader",
            "Custom Reader",
            string.Empty,
            ["platform.tenant.read"],
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
                CancellationToken.None);
            roles.AddRange(page.Items);
            token = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(token));

        Assert.Equal(AuthorizationCatalog.SystemRoles.Count + 1, roles.Count);
        Assert.Contains(roles, static role => role.Key == "custom-reader");
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
