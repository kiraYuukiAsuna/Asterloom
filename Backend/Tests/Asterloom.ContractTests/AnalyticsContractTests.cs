using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Sdk.Analytics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class AnalyticsContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] CheckoutEventNames = ["checkout.completed"];
    private readonly WebApplicationFactory<Program> _factory;

    public AnalyticsContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task JsonTranscodingAndSdkCoverCompleteAnalyticsLifecycle()
    {
        using var client = await CreateAuthorizedClientAsync();
        var scope = await CreateScopeAsync(client);
        var analyticsPath = ScopePath(scope) + "/analytics";
        var schema = await SendAsync<EventSchemaJson>(client.PostAsJsonAsync(
            analyticsPath + "/schemas",
            new
            {
                key = "checkout.completed",
                displayName = "Checkout completed",
                description = "Successful checkout outcome.",
                schemaJson =
                    """
                    {
                      "type":"object",
                      "additionalProperties":false,
                      "required":["orderId","amount","cardToken"],
                      "properties":{
                        "orderId":{"type":"string"},
                        "amount":{"type":"number"},
                        "cardToken":{"type":"string","x-asterloom-sensitive":true}
                      }
                    }
                    """,
                retentionDays = 90,
            }));
        var schemas = await client.GetFromJsonAsync<EventSchemaListJson>(
            analyticsPath + "/schemas?pageSize=20");
        Assert.Contains(schemas!.EventSchemas, item => item.Id == schema.Id);
        var fetchedSchema = await client.GetFromJsonAsync<EventSchemaJson>(
            analyticsPath + $"/schemas/{schema.Id}");
        Assert.Equal(schema.Id, fetchedSchema!.Id);

        schema = await SendAsync<EventSchemaJson>(client.PatchAsJsonAsync(
            analyticsPath + $"/schemas/{schema.Id}",
            new
            {
                displayName = "Checkout completed v2",
                description = schema.Description,
                schemaJson = schema.SchemaJson,
                expectedVersion = schema.Version,
            }));
        schema = await SendAsync<EventSchemaJson>(client.PatchAsJsonAsync(
            analyticsPath + $"/schemas/{schema.Id}/retention",
            new { retentionDays = 120, expectedVersion = schema.Version }));
        Assert.Equal(120, schema.RetentionDays);
        schema = await SendAsync<EventSchemaJson>(client.DeleteAsync(
            analyticsPath + $"/schemas/{schema.Id}?expectedVersion={schema.Version}"));
        Assert.Equal("ANALYTICS_RESOURCE_STATUS_ARCHIVED", schema.Status);
        schema = await SendAsync<EventSchemaJson>(client.PostAsJsonAsync(
            analyticsPath + $"/schemas/{schema.Id}:restore",
            new { expectedVersion = schema.Version }));
        Assert.Equal("ANALYTICS_RESOURCE_STATUS_ACTIVE", schema.Status);

        var credential = await SendAsync<WriteKeyCredentialJson>(client.PostAsJsonAsync(
            analyticsPath + "/write-keys",
            new { name = "Contract SDK" }));
        Assert.StartsWith("ast_an_", credential.Secret, StringComparison.Ordinal);
        var writeKeys = await client.GetFromJsonAsync<WriteKeyListJson>(
            analyticsPath + "/write-keys");
        Assert.Contains(writeKeys!.WriteKeys, item => item.Id == credential.WriteKey.Id);
        credential = await SendAsync<WriteKeyCredentialJson>(client.PostAsJsonAsync(
            analyticsPath + $"/write-keys/{credential.WriteKey.Id}:rotate",
            new { expectedVersion = credential.WriteKey.Version }));

        await using (var sdk = new AsterloomAnalyticsClient(
            client,
            new AsterloomAnalyticsClientOptions
            {
                WriteKey = credential.Secret,
                BatchSize = 10,
                CompressionThresholdBytes = 1,
                FlushInterval = TimeSpan.FromMinutes(1),
                MaximumRetries = 0,
                CommonContext = new Dictionary<string, object?>
                {
                    ["applicationVersion"] = "1.0.0",
                    ["platform"] = "contract-test",
                },
            }))
        {
            await sdk.TrackAsync(
                "checkout.completed",
                new { orderId = "order-42", amount = 19.95, cardToken = "secret-value" },
                new AsterloomAnalyticsIdentity(
                    ActorId: "contract-user",
                    SessionId: "session-42"));
            var flush = await sdk.FlushAsync();
            Assert.Equal(1, flush.Accepted);
            Assert.Equal(0, flush.Remaining);
        }

        const string duplicateEventId = "analytics-contract-dedup";
        var runtimePayload = new
        {
            events = new[]
            {
                new
                {
                    eventId = duplicateEventId,
                    eventName = "checkout.completed",
                    occurredAt = DateTimeOffset.UtcNow,
                    actorId = "contract-user",
                    sessionId = "session-42",
                    propertiesJson =
                        "{\"orderId\":\"order-43\",\"amount\":20.0,\"cardToken\":\"sensitive\"}",
                    contextJson = "{\"platform\":\"contract-test\"}",
                    sdkName = "contract",
                    sdkVersion = "1.0.0",
                },
            },
        };
        using (var ingestionClient = _factory.CreateClient())
        {
            ingestionClient.DefaultRequestHeaders.Add("X-Asterloom-Write-Key", credential.Secret);
            var first = await SendAsync<IngestionJson>(ingestionClient.PostAsJsonAsync(
                "/api/v1/analytics/events:batch",
                runtimePayload));
            var second = await SendAsync<IngestionJson>(ingestionClient.PostAsJsonAsync(
                "/api/v1/analytics/events:batch",
                runtimePayload));
            Assert.Equal(1, first.Accepted);
            Assert.Equal(1, second.Deduplicated);
        }

        var events = await client.GetFromJsonAsync<EventListJson>(
            analyticsPath + "/events?pageSize=20&eventName=checkout.completed");
        Assert.True(events!.Events.Count >= 2);
        Assert.All(events.Events, item => Assert.Contains("[REDACTED]", item.PropertiesJson));
        var analyticsEvent = await client.GetFromJsonAsync<EventJson>(
            analyticsPath + $"/events/{events.Events[0].Id}");
        Assert.Equal(events.Events[0].Id, analyticsEvent!.Id);

        var query = await SendAsync<QueryJson>(client.PostAsJsonAsync(
            analyticsPath + ":query",
            new
            {
                eventNames = CheckoutEventNames,
                fromAt = DateTimeOffset.UtcNow.AddHours(-1),
                toAt = DateTimeOffset.UtcNow.AddHours(1),
                interval = "hour",
            }));
        Assert.Equal(2, query.Buckets.Sum(static bucket => bucket.EventCount));
        var export = await SendAsync<ExportJson>(client.PostAsJsonAsync(
            analyticsPath + "/events:export",
            new { eventName = "checkout.completed", maximumRows = 100 }));
        Assert.Equal(2, export.ExportedRows);
        Assert.Contains("checkout.completed", Encoding.UTF8.GetString(export.Content));

        var revoked = await SendAsync<WriteKeyJson>(client.PostAsJsonAsync(
            analyticsPath + $"/write-keys/{credential.WriteKey.Id}:revoke",
            new { expectedVersion = credential.WriteKey.Version }));
        Assert.Equal("ANALYTICS_WRITE_KEY_STATUS_REVOKED", revoked.Status);
    }

    private static async Task<ScopeResources> CreateScopeAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            "/api/v1/tenants",
            new { slug = $"analytics-{suffix}", displayName = "Analytics Tenant" }));
        var application = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications",
            new { slug = $"product-{suffix}", displayName = "Analytics App" }));
        var environment = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications/{application.Id}/environments",
            new
            {
                slug = "production",
                displayName = "Production",
                environmentType = "ENVIRONMENT_TYPE_PRODUCTION",
                isProtected = false,
            }));
        return new(tenant.Id, application.Id, environment.Id);
    }

    private static string ScopePath(ScopeResources resources) =>
        $"/api/v1/tenants/{resources.TenantId}/applications/{resources.ApplicationId}" +
        $"/environments/{resources.EnvironmentId}";

    private static async Task<T> SendAsync<T>(Task<HttpResponseMessage> responseTask)
    {
        using var response = await responseTask;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected success but got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("The JSON response was empty.");
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        const string clientId = "analytics-contract-tests";
        const string clientSecret = "Analytics-Contract-Tests-Secret!2026";
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
                    DisplayName = "Analytics contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }

            var store = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
            var bindingId = Guid.Parse("abababab-abab-7bab-8bab-abababababab");
            if (await store.GetRoleBindingAsync(bindingId, CancellationToken.None) is null)
            {
                var management = scope.ServiceProvider
                    .GetRequiredService<AuthorizationManagementService>();
                await management.SetRoleBindingAsync(
                    bindingId.ToString(),
                    clientId,
                    AuthorizationCatalog.FindSystemRole("super-administrator")!.Id.ToString(),
                    AuthorizationScope.Global,
                    0,
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
        string EnvironmentId);
    private sealed record ResourceJson(string Id);
    private sealed record EventSchemaJson(
        string Id,
        string Description,
        string SchemaJson,
        string Status,
        int RetentionDays,
        long Version);
    private sealed record EventSchemaListJson(IReadOnlyList<EventSchemaJson> EventSchemas);
    private sealed record WriteKeyJson(string Id, string Status, long Version);
    private sealed record WriteKeyCredentialJson(WriteKeyJson WriteKey, string Secret);
    private sealed record WriteKeyListJson(IReadOnlyList<WriteKeyJson> WriteKeys);
    private sealed record IngestionJson(int Accepted, int Rejected, int Deduplicated);
    private sealed record EventJson(string Id, string PropertiesJson);
    private sealed record EventListJson(IReadOnlyList<EventJson> Events);
    private sealed record BucketJson(long EventCount);
    private sealed record QueryJson(IReadOnlyList<BucketJson> Buckets);
    private sealed record ExportJson(byte[] Content, int ExportedRows);
}
