using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Sdk.Feature;
using Asterloom.Targeting;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenFeature.Constant;
using OpenFeature.Model;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class FeatureContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory;

    public FeatureContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task JsonTranscodingManagesTheCompleteFeatureSurface()
    {
        using var client = await CreateAuthorizedClientAsync();
        var resources = await CreateScopeAndSegmentAsync(client, "json-feature");
        var basePath = ScopePath(resources);
        var flag = await SendAsync<FeatureFlagJson>(client.PostAsJsonAsync(
            $"{basePath}/flags",
            new
            {
                key = "new-home",
                displayName = "New home",
                description = "JSON Transcoding contract flag",
                valueKind = "FEATURE_VALUE_KIND_BOOLEAN",
                definition = DefinitionPayload(resources.SegmentId, enabled: true),
            }));
        Assert.Equal("new-home", flag.Key);

        var fetched = await client.GetFromJsonAsync<FeatureFlagJson>(
            $"{basePath}/flags/{flag.Id}");
        Assert.Equal(flag.Id, fetched!.Id);
        flag = await SendAsync<FeatureFlagJson>(client.PatchAsJsonAsync(
            $"{basePath}/flags/{flag.Id}/draft",
            new
            {
                displayName = "New home updated",
                description = "Updated through JSON Transcoding",
                definition = DefinitionPayload(resources.SegmentId, enabled: true),
                expectedVersion = flag.Version,
            }));
        var validation = await SendAsync<FeatureValidationJson>(client.PostAsJsonAsync(
            $"{basePath}/flags/{flag.Id}:validate",
            new { }));
        Assert.True(validation.Valid);
        Assert.NotEmpty(validation.DefinitionHash);

        flag = await SendAsync<FeatureFlagJson>(client.PostAsJsonAsync(
            $"{basePath}/flags/{flag.Id}:publish",
            new { expectedVersion = flag.Version }));
        Assert.Equal(1, flag.PublishedRevision);
        var flags = await client.GetFromJsonAsync<FeatureFlagListJson>(
            $"{basePath}/flags?pageSize=20&query=new-home");
        Assert.Contains(flags!.Flags, candidate => candidate.Id == flag.Id);
        var revisions = await client.GetFromJsonAsync<FeatureRevisionListJson>(
            $"{basePath}/flags/{flag.Id}/revisions?pageSize=20");
        Assert.Single(revisions!.Revisions);

        var context = ContextPayload("contract-user", "CN");
        var simulation = await SendAsync<FeatureEvaluationJson>(client.PostAsJsonAsync(
            $"{basePath}/flags/{flag.Id}:simulate",
            new { useDraft = false, context }));
        Assert.Equal("on", simulation.VariantKey);
        Assert.Equal("FEATURE_EVALUATION_REASON_TARGETING_MATCH", simulation.Reason);
        var runtime = await SendAsync<FeatureEvaluationJson>(client.PostAsJsonAsync(
            $"{basePath}/flags/{flag.Key}:evaluate",
            new
            {
                flagKey = flag.Key,
                expectedKind = "FEATURE_VALUE_KIND_BOOLEAN",
                context,
            }));
        Assert.Equal(simulation.VariantKey, runtime.VariantKey);

        flag = await SendAsync<FeatureFlagJson>(client.PatchAsJsonAsync(
            $"{basePath}/flags/{flag.Id}/draft",
            new
            {
                displayName = flag.DisplayName,
                description = "Disabled second revision",
                definition = DefinitionPayload(resources.SegmentId, enabled: false),
                expectedVersion = flag.Version,
            }));
        flag = await SendAsync<FeatureFlagJson>(client.PostAsJsonAsync(
            $"{basePath}/flags/{flag.Id}:publish",
            new { expectedVersion = flag.Version }));
        Assert.Equal(2, flag.PublishedRevision);
        flag = await SendAsync<FeatureFlagJson>(client.PostAsJsonAsync(
            $"{basePath}/flags/{flag.Id}:rollback",
            new { revision = 1, expectedVersion = flag.Version }));
        Assert.Equal(3, flag.PublishedRevision);

        flag = await SendAsync<FeatureFlagJson>(client.DeleteAsync(
            $"{basePath}/flags/{flag.Id}?expectedVersion={flag.Version}"));
        Assert.Equal("FEATURE_RESOURCE_STATUS_ARCHIVED", flag.Status);
        flags = await client.GetFromJsonAsync<FeatureFlagListJson>(
            $"{basePath}/flags?pageSize=20&includeArchived=true");
        Assert.Contains(flags!.Flags, candidate => candidate.Id == flag.Id);
        flag = await SendAsync<FeatureFlagJson>(client.PostAsJsonAsync(
            $"{basePath}/flags/{flag.Id}:restore",
            new { expectedVersion = flag.Version }));
        Assert.Equal("FEATURE_RESOURCE_STATUS_ACTIVE", flag.Status);
    }

    [Fact]
    public async Task NativeGrpcFeatureSdkAndOpenFeatureProviderCoverTheCompleteSurface()
    {
        using var httpClient = await CreateAuthorizedClientAsync();
        var resources = await CreateScopeAndSegmentAsync(httpClient, "sdk-feature");
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient });
        var sdk = new AsterloomFeatureAdminClient(channel.CreateCallInvoker());
        var scope = new AsterloomFeatureScope(
            Guid.Parse(resources.TenantId),
            Guid.Parse(resources.ApplicationId),
            Guid.Parse(resources.EnvironmentId));
        var definition = Definition(Guid.Parse(resources.SegmentId), enabled: true);

        var flag = await sdk.CreateFlagAsync(
            scope,
            new AsterloomFeatureRegistration(
                "sdk-new-home",
                "SDK new home",
                "Native SDK contract flag",
                AsterloomFeatureValueKind.Truth,
                definition));
        flag = await sdk.GetFlagAsync(scope, flag.Id);
        flag = await sdk.UpdateFlagDraftAsync(
            flag,
            new AsterloomFeatureDraftUpdate(
                "SDK new home updated",
                flag.Description,
                definition));
        var validation = await sdk.ValidateFlagDraftAsync(flag);
        Assert.True(validation.Valid);
        flag = await sdk.PublishFlagAsync(flag);
        var flags = await sdk.ListFlagsAsync(scope, "sdk-new-home");
        Assert.Single(flags.Items);
        var revisions = await sdk.ListFlagRevisionsAsync(flag);
        Assert.Single(revisions.Items);

        var context = new TargetingEvaluationContext(
            "sdk-contract-user",
            scope.ApplicationId,
            scope.EnvironmentId,
            region: "CN");
        var simulation = await sdk.SimulateFlagAsync(flag, context);
        Assert.Equal("on", simulation.VariantKey);
        var provider = new AsterloomFeatureProvider(
            channel.CreateCallInvoker(),
            new AsterloomFeatureProviderOptions
            {
                Scope = scope,
                CacheDuration = TimeSpan.FromMinutes(1),
            });
        var openFeatureContext = EvaluationContext.Builder()
            .SetTargetingKey("sdk-contract-user")
            .Set("region", "CN")
            .Build();
        var resolved = await provider.ResolveBooleanValueAsync(
            flag.Key,
            defaultValue: false,
            openFeatureContext);
        Assert.True(resolved.Value);
        Assert.Equal("on", resolved.Variant);
        Assert.Equal(ErrorType.None, resolved.ErrorType);

        flag = await sdk.UpdateFlagDraftAsync(
            flag,
            new AsterloomFeatureDraftUpdate(
                flag.DisplayName,
                "Disabled second revision",
                Definition(Guid.Parse(resources.SegmentId), enabled: false)));
        flag = await sdk.PublishFlagAsync(flag);
        flag = await sdk.RollbackFlagAsync(flag, revision: 1);
        Assert.Equal(3, flag.PublishedRevision);
        flag = await sdk.ArchiveFlagAsync(flag);
        Assert.Equal(AsterloomFeatureResourceStatus.Archived, flag.Status);
        flag = await sdk.RestoreFlagAsync(flag);
        Assert.Equal(AsterloomFeatureResourceStatus.Active, flag.Status);
    }

    private static async Task<ScopeResources> CreateScopeAndSegmentAsync(
        HttpClient client,
        string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            "/api/v1/tenants",
            new { slug = $"{prefix}-{suffix}", displayName = "Feature Tenant" }));
        var application = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications",
            new { slug = $"{prefix}-{suffix}", displayName = "Feature App" }));
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
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments/{environment.Id}/targeting/segments",
            new
            {
                key = "feature-contract-users",
                displayName = "Feature contract users",
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

    private static AsterloomFeatureDefinition Definition(Guid segmentId, bool enabled) => new(
        enabled,
        "off",
        [
            new AsterloomFeatureVariant("off", "Off", AsterloomFeatureValue.From(false)),
            new AsterloomFeatureVariant("on", "On", AsterloomFeatureValue.From(true)),
        ],
        [],
        [new AsterloomFeatureTargetingRule("contract-segment", segmentId, "on")],
        [
            new AsterloomFeatureAllocation("off", 0, 50_000),
            new AsterloomFeatureAllocation("on", 50_000, 100_000),
        ],
        "stable-contract-salt");

    private static object DefinitionPayload(string segmentId, bool enabled) => new
    {
        enabled,
        defaultVariantKey = "off",
        variants = new object[]
        {
            new { key = "off", displayName = "Off", value = new { booleanValue = false } },
            new { key = "on", displayName = "On", value = new { booleanValue = true } },
        },
        prerequisites = Array.Empty<object>(),
        targetingRules = new[]
        {
            new { id = "contract-segment", segmentId, variantKey = "on" },
        },
        allocations = new[]
        {
            new { variantKey = "off", start = 0, end = 50_000 },
            new { variantKey = "on", start = 50_000, end = 100_000 },
        },
        bucketingSalt = "stable-contract-salt",
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
            $"Expected a success response but received {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("The JSON response was empty.");
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        const string clientId = "feature-contract-tests";
        const string clientSecret = "Feature-Contract-Tests-Secret!2026";
        using (var scope = _factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            if (await manager.FindByClientIdAsync(clientId) is null)
            {
                await manager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    ClientType = ClientTypes.Confidential,
                    DisplayName = "Feature contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }

            var authorizationStore = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
            var bindingId = Guid.Parse("bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb");
            if (await authorizationStore.GetRoleBindingAsync(
                    bindingId,
                    CancellationToken.None) is null)
            {
                var management = scope.ServiceProvider
                    .GetRequiredService<AuthorizationManagementService>();
                var superAdministrator = AuthorizationCatalog.FindSystemRole(
                    "super-administrator")!;
                await management.SetRoleBindingAsync(
                    bindingId.ToString("D"),
                    clientId,
                    superAdministrator.Id.ToString("D"),
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

    private sealed record FeatureFlagJson(
        string Id,
        string Key,
        string DisplayName,
        string Status,
        long DraftRevision,
        long PublishedRevision,
        long Version);

    private sealed record FeatureFlagListJson(IReadOnlyList<FeatureFlagJson> Flags);

    private sealed record FeatureRevisionJson(long Revision, long SourceRevision);

    private sealed record FeatureRevisionListJson(
        IReadOnlyList<FeatureRevisionJson> Revisions);

    private sealed record FeatureValidationJson(
        bool Valid,
        IReadOnlyList<JsonElement> Issues,
        string DefinitionHash);

    private sealed record FeatureEvaluationJson(
        string VariantKey,
        string Reason,
        JsonElement Value,
        long Revision,
        bool UsedDraft);
}
