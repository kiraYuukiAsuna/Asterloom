using System.Text;
using Asterloom.Modules.Auditing;
using Asterloom.Modules.Hosting;
using Asterloom.Modules.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class AuditTests
{
    [Fact]
    public async Task AuditEventsAreAppendOnlyFilterableAndExportable()
    {
        var now = new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(now);
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditStore>();
        var management = scope.ServiceProvider.GetRequiredService<AuditManagementService>();
        var tenantId = Guid.CreateVersion7();
        var succeededId = Guid.CreateVersion7();
        await store.AppendAsync(
            new AsterloomAuditEvent(
                succeededId,
                "admin:one",
                tenantId,
                null,
                null,
                "/asterloom.platform.admin.v1.PlatformAdminService/CreateTenant",
                "tenant",
                Guid.CreateVersion7().ToString("D"),
                "request-success",
                AuditOutcome.Succeeded,
                string.Empty,
                "request_fields=[slug,display_name]",
                now),
            CancellationToken.None);
        await store.AppendAsync(
            new AsterloomAuditEvent(
                Guid.CreateVersion7(),
                "=HYPERLINK(\"https://invalid.example\")",
                null,
                null,
                null,
                "/asterloom.platform.admin.v1.PlatformAdminService/CreateTenant",
                "tenant",
                string.Empty,
                "request-denied",
                AuditOutcome.Denied,
                "permission_denied",
                "request_fields=[slug,display_name]",
                now.AddMinutes(1)),
            CancellationToken.None);

        var filtered = await management.ListAsync(
            pageSize: 10,
            pageToken: null,
            actorId: "ONE",
            operation: "CreateTenant",
            outcome: AuditOutcome.Succeeded,
            requestId: "request-success",
            fromAt: now.AddMinutes(-1),
            toAt: now.AddMinutes(1),
            CancellationToken.None);
        var fetched = await management.GetAsync(
            succeededId.ToString("D"),
            CancellationToken.None);
        var export = await management.ExportAsync(
            actorId: null,
            operation: "CreateTenant",
            outcome: null,
            requestId: null,
            fromAt: null,
            toAt: null,
            maximumRows: 10,
            CancellationToken.None);

        Assert.Single(filtered.Items);
        Assert.Equal(succeededId, fetched.Id);
        Assert.Equal(2, export.ExportedRows);
        var csv = Encoding.UTF8.GetString(export.Content);
        Assert.Contains("created_at,outcome,actor_id", csv, StringComparison.Ordinal);
        Assert.Contains("permission_denied", csv, StringComparison.Ordinal);
        Assert.Contains("'=HYPERLINK", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditPagingDoesNotDuplicateOrSkipEvents()
    {
        await using var provider = CreateProvider(DateTimeOffset.UtcNow);
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditStore>();
        var management = scope.ServiceProvider.GetRequiredService<AuditManagementService>();
        var timestamp = new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 3; index++)
        {
            await store.AppendAsync(
                new AsterloomAuditEvent(
                    Guid.CreateVersion7(),
                    "admin",
                    null,
                    null,
                    null,
                    "operation",
                    "resource",
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"request-{index}",
                    AuditOutcome.Succeeded,
                    string.Empty,
                    "request_fields=[]",
                    timestamp.AddSeconds(index)),
                CancellationToken.None);
        }

        var first = await management.ListAsync(
            2, null, null, null, null, null, null, null, CancellationToken.None);
        var second = await management.ListAsync(
            2,
            first.NextPageToken,
            null,
            null,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Empty(second.NextPageToken);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(item => item.Id).Distinct().Count());
    }

    private static ServiceProvider CreateProvider(DateTimeOffset now)
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
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddAsterloomModules(
            configuration,
            new AuditModule(),
            new InfrastructureModule());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
