using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Sdk.Config;
using Asterloom.Targeting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class ConfigContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory;

    public ConfigContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task JsonTranscodingAndHttpSdkCoverTheCompleteConfigSurface()
    {
        using var client = await CreateAuthorizedClientAsync();
        var resources = await CreateScopeAndSegmentAsync(client);
        var basePath = ScopePath(resources);
        var entry = await SendAsync<ConfigEntryJson>(client.PostAsJsonAsync(
            $"{basePath}/config/entries",
            new
            {
                key = "ui.banner",
                displayName = "UI banner",
                description = "Configuration JSON contract.",
                valueKind = "CONFIG_VALUE_KIND_STRING",
                visibility = "CONFIG_VISIBILITY_CLIENT",
                definition = DefinitionPayload(resources.SegmentId, "stable", "preview"),
            }));
        Assert.Equal("ui.banner", entry.Key);

        var fetched = await client.GetFromJsonAsync<ConfigEntryJson>(
            $"{basePath}/config/entries/{entry.Id}");
        Assert.Equal(entry.Id, fetched!.Id);
        entry = await SendAsync<ConfigEntryJson>(client.PatchAsJsonAsync(
            $"{basePath}/config/entries/{entry.Id}/draft",
            new
            {
                displayName = "UI banner updated",
                description = "Updated configuration JSON contract.",
                visibility = "CONFIG_VISIBILITY_CLIENT",
                definition = DefinitionPayload(resources.SegmentId, "stable", "preview"),
                expectedVersion = entry.Version,
            }));
        var validation = await SendAsync<ValidationJson>(client.PostAsJsonAsync(
            $"{basePath}/config/entries/{entry.Id}:validate",
            new { }));
        Assert.True(validation.Valid);
        var diff = await client.GetFromJsonAsync<DiffJson>(
            $"{basePath}/config/entries/{entry.Id}/diff");
        Assert.True(diff!.Changed);
        entry = await SendAsync<ConfigEntryJson>(client.PostAsJsonAsync(
            $"{basePath}/config/entries/{entry.Id}:publish",
            new { expectedVersion = entry.Version }));
        Assert.Equal(1, entry.PublishedRevision);

        var entries = await client.GetFromJsonAsync<EntryListJson>(
            $"{basePath}/config/entries?pageSize=20&query=ui.banner");
        Assert.Contains(entries!.Entries, candidate => candidate.Id == entry.Id);
        var revisions = await client.GetFromJsonAsync<RevisionListJson>(
            $"{basePath}/config/entries/{entry.Id}/revisions?pageSize=20");
        Assert.Single(revisions!.Revisions);
        var context = ContextPayload("config-contract-user", "CN");
        var preview = await SendAsync<EffectiveValueJson>(client.PostAsJsonAsync(
            $"{basePath}/config/entries/{entry.Id}:preview",
            new { useDraft = false, context }));
        Assert.Equal("preview", preview.Value.GetProperty("stringValue").GetString());

        var snapshot = await SendAsync<SnapshotJson>(client.PostAsJsonAsync(
            $"{basePath}/config:snapshot",
            new { context, ifNoneMatch = string.Empty }));
        Assert.Single(snapshot.Values);
        var notModified = await SendAsync<SnapshotJson>(client.PostAsJsonAsync(
            $"{basePath}/config:snapshot",
            new { context, ifNoneMatch = snapshot.Etag }));
        Assert.True(notModified.NotModified);
        var update = await SendAsync<UpdateStatusJson>(client.PostAsJsonAsync(
            $"{basePath}/config:check-updates",
            new { knownSnapshotVersion = 0, context }));
        Assert.True(update.Changed);

        var serverEntry = await SendAsync<ConfigEntryJson>(client.PostAsJsonAsync(
            $"{basePath}/config/entries",
            new
            {
                key = "internal.batch-size",
                displayName = "Internal batch size",
                description = "Server-visible contract entry.",
                valueKind = "CONFIG_VALUE_KIND_INTEGER",
                visibility = "CONFIG_VISIBILITY_SERVER",
                definition = new
                {
                    schemaJson = "{\"type\":\"integer\",\"minimum\":1}",
                    defaultValue = new { integerValue = 25 },
                    targetingRules = Array.Empty<object>(),
                },
            }));
        serverEntry = await SendAsync<ConfigEntryJson>(client.PostAsJsonAsync(
            $"{basePath}/config/entries/{serverEntry.Id}:publish",
            new { expectedVersion = serverEntry.Version }));
        var clientOnly = await SendAsync<SnapshotJson>(client.PostAsJsonAsync(
            $"{basePath}/config:snapshot",
            new { context }));
        Assert.DoesNotContain(clientOnly.Values, item => item.Key == serverEntry.Key);
        var serverSnapshot = await SendAsync<SnapshotJson>(client.PostAsJsonAsync(
            $"{basePath}/config:server-snapshot",
            new { context }));
        Assert.Contains(serverSnapshot.Values, item => item.Key == serverEntry.Key);

        var snapshots = await client.GetFromJsonAsync<SnapshotListJson>(
            $"{basePath}/config/snapshots?pageSize=20");
        Assert.Equal(2, snapshots!.Snapshots.Count);
        entry = await SendAsync<ConfigEntryJson>(client.PatchAsJsonAsync(
            $"{basePath}/config/entries/{entry.Id}/draft",
            new
            {
                displayName = entry.DisplayName,
                description = "Second revision.",
                visibility = "CONFIG_VISIBILITY_CLIENT",
                definition = DefinitionPayload(resources.SegmentId, "second", "second-preview"),
                expectedVersion = entry.Version,
            }));
        entry = await SendAsync<ConfigEntryJson>(client.PostAsJsonAsync(
            $"{basePath}/config/entries/{entry.Id}:publish",
            new { expectedVersion = entry.Version }));
        entry = await SendAsync<ConfigEntryJson>(client.PostAsJsonAsync(
            $"{basePath}/config/entries/{entry.Id}:rollback",
            new { revision = 1, expectedVersion = entry.Version }));
        Assert.Equal(3, entry.PublishedRevision);
        entry = await SendAsync<ConfigEntryJson>(client.DeleteAsync(
            $"{basePath}/config/entries/{entry.Id}?expectedVersion={entry.Version}"));
        Assert.Equal("CONFIG_RESOURCE_STATUS_ARCHIVED", entry.Status);
        entry = await SendAsync<ConfigEntryJson>(client.PostAsJsonAsync(
            $"{basePath}/config/entries/{entry.Id}:restore",
            new { expectedVersion = entry.Version }));
        Assert.Equal("CONFIG_RESOURCE_STATUS_ACTIVE", entry.Status);

        var scope = new AsterloomConfigScope(
            Guid.Parse(resources.TenantId),
            Guid.Parse(resources.ApplicationId),
            Guid.Parse(resources.EnvironmentId));
        using var sdk = new AsterloomConfigClient(
            client,
            new AsterloomConfigClientOptions
            {
                Scope = scope,
                CacheDuration = TimeSpan.Zero,
                LastKnownGoodDuration = TimeSpan.FromMinutes(5),
            });
        var sdkContext = AsterloomConfigContext.Create(
            scope,
            "sdk-config-user",
            region: "CN");
        var value = await sdk.GetStringAsync(
            entry.Key,
            "fallback",
            sdkContext);
        Assert.Equal("preview", value);
        var firstSdkSnapshot = await sdk.GetSnapshotAsync(
            sdkContext,
            forceRefresh: true);
        var secondSdkSnapshot = await sdk.GetSnapshotAsync(
            sdkContext,
            forceRefresh: true);
        Assert.Equal(firstSdkSnapshot.ETag, secondSdkSnapshot.ETag);
        Assert.False(secondSdkSnapshot.IsLastKnownGood);
        var sdkUpdate = await sdk.CheckForUpdatesAsync(sdkContext, 0);
        Assert.True(sdkUpdate.Changed);
    }

    private static async Task<ScopeResources> CreateScopeAndSegmentAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            "/api/v1/tenants",
            new { slug = $"config-{suffix}", displayName = "Config Tenant" }));
        var application = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications",
            new { slug = $"config-{suffix}", displayName = "Config App" }));
        var environment = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments",
            new
            {
                slug = "production",
                displayName = "Production",
                environmentType = "ENVIRONMENT_TYPE_PRODUCTION",
                isProtected = false,
            }));
        var segment = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}"
            + $"/environments/{environment.Id}/targeting/segments",
            new
            {
                key = "config-contract-users",
                displayName = "Config contract users",
                description = "Matches CN users.",
                rule = new
                {
                    matchMode = "TARGETING_MATCH_MODE_ALL",
                    conditions = new[]
                    {
                        new
                        {
                            id = "region",
                            attribute = "region",
                            valueKind = "TARGETING_VALUE_KIND_TEXT",
                            @operator = "TARGETING_OPERATOR_EQUALS",
                            values = new[] { new { text = "cn" } },
                            caseSensitive = false,
                        },
                    },
                },
            }));
        return new(tenant.Id, application.Id, environment.Id, segment.Id);
    }

    private static object DefinitionPayload(
        string segmentId,
        string defaultValue,
        string targetedValue) => new
        {
            schemaJson = "{\"type\":\"string\",\"minLength\":1}",
            defaultValue = new { stringValue = defaultValue },
            targetingRules = new[]
            {
                new
                {
                    id = "cn-preview",
                    segmentId,
                    value = new { stringValue = targetedValue },
                },
            },
        };

    private static object ContextPayload(string targetingKey, string region) => new
    {
        targetingKey,
        region,
        attributes = Array.Empty<object>(),
    };

    private static string ScopePath(ScopeResources resources) =>
        $"/api/v1/tenants/{resources.TenantId}/applications/{resources.ApplicationId}"
        + $"/environments/{resources.EnvironmentId}";

    private static async Task<T> SendAsync<T>(Task<HttpResponseMessage> responseTask)
    {
        using var response = await responseTask;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected success but received {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("The JSON response was empty.");
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        const string clientId = "config-contract-tests";
        const string clientSecret = "Config-Contract-Tests-Secret!2026";
        using (var serviceScope = _factory.Services.CreateScope())
        {
            var manager = serviceScope.ServiceProvider
                .GetRequiredService<IOpenIddictApplicationManager>();
            if (await manager.FindByClientIdAsync(clientId) is null)
            {
                await manager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    ClientType = ClientTypes.Confidential,
                    DisplayName = "Config contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }
            var authorizationStore = serviceScope.ServiceProvider
                .GetRequiredService<IAuthorizationStore>();
            var bindingId = Guid.Parse("cccccccc-cccc-7ccc-8ccc-cccccccccccc");
            if (await authorizationStore.GetRoleBindingAsync(
                    bindingId,
                    CancellationToken.None) is null)
            {
                var management = serviceScope.ServiceProvider
                    .GetRequiredService<AuthorizationManagementService>();
                await management.SetRoleBindingAsync(
                    bindingId.ToString("D"),
                    clientId,
                    AuthorizationCatalog.FindSystemRole("super-administrator")!.Id.ToString("D"),
                    AuthorizationScope.Global,
                    expectedVersion: 0,
                    CancellationToken.None);
            }
        }

        var client = _factory.CreateClient();
        using var tokenResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.ClientCredentials,
                [Parameters.ClientId] = clientId,
                [Parameters.ClientSecret] = clientSecret,
                [Parameters.Scope] = "asterloom.api",
            }));
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.GetProperty(Parameters.AccessToken).GetString());
        return client;
    }

    private sealed record ScopeResources(
        string TenantId,
        string ApplicationId,
        string EnvironmentId,
        string SegmentId);

    private sealed record ResourceJson(string Id);

    private sealed record ConfigEntryJson(
        string Id,
        string Key,
        string DisplayName,
        string Status,
        long DraftRevision,
        long PublishedRevision,
        long Version);

    private sealed record EntryListJson(IReadOnlyList<ConfigEntryJson> Entries);

    private sealed record ValidationJson(bool Valid, string DefinitionHash);

    private sealed record DiffJson(bool Changed, IReadOnlyList<string> ChangedPaths);

    private sealed record RevisionListJson(IReadOnlyList<JsonElement> Revisions);

    private sealed record EffectiveValueJson(string Key, JsonElement Value);

    private sealed record SnapshotJson(
        long SnapshotVersion,
        string Etag,
        bool NotModified,
        IReadOnlyList<EffectiveValueJson> Values);

    private sealed record UpdateStatusJson(bool Changed, long CurrentSnapshotVersion);

    private sealed record SnapshotListJson(IReadOnlyList<JsonElement> Snapshots);
}
