using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Modules.Telemetry.Model;
using Asterloom.Modules.Telemetry.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class TelemetryContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] CollectorStatuses =
    [
        "COLLECTOR_HEALTH_STATUS_HEALTHY",
        "COLLECTOR_HEALTH_STATUS_DEGRADED",
        "COLLECTOR_HEALTH_STATUS_UNAVAILABLE",
    ];
    private readonly WebApplicationFactory<Program> _factory;

    public TelemetryContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task JsonTranscodingCoversCompleteTelemetryManagementSurface()
    {
        using var client = await CreateAuthorizedClientAsync();
        var scope = await CreateScopeAsync(client);
        var telemetryPath = ScopePath(scope) + "/telemetry";

        var source = await SendAsync<TelemetrySourceJson>(client.PostAsJsonAsync(
            telemetryPath + "/sources",
            new
            {
                key = "checkout-api",
                displayName = "Checkout API",
                description = "Checkout technical signals.",
                serviceName = "asterloom.checkout-api",
                resourceAttributesJson = "{\"team.name\":\"payments\"}",
            }));
        var sources = await client.GetFromJsonAsync<TelemetrySourceListJson>(
            telemetryPath + "/sources?pageSize=20");
        Assert.Contains(sources!.Sources, item => item.Id == source.Id);
        var fetched = await client.GetFromJsonAsync<TelemetrySourceJson>(
            telemetryPath + $"/sources/{source.Id}");
        Assert.Equal(source.Id, fetched!.Id);

        source = await SendAsync<TelemetrySourceJson>(client.PatchAsJsonAsync(
            telemetryPath + $"/sources/{source.Id}",
            new
            {
                displayName = "Checkout API v2",
                description = source.Description,
                serviceName = "asterloom.checkout-api-v2",
                resourceAttributesJson = source.ResourceAttributesJson,
                expectedVersion = source.Version,
            }));
        Assert.Equal("Checkout API v2", source.DisplayName);
        source = await SendAsync<TelemetrySourceJson>(client.DeleteAsync(
            telemetryPath + $"/sources/{source.Id}?expectedVersion={source.Version}"));
        Assert.Equal("TELEMETRY_RESOURCE_STATUS_ARCHIVED", source.Status);
        source = await SendAsync<TelemetrySourceJson>(client.PostAsJsonAsync(
            telemetryPath + $"/sources/{source.Id}:restore",
            new { expectedVersion = source.Version }));
        Assert.Equal("TELEMETRY_RESOURCE_STATUS_ACTIVE", source.Status);

        var settings = await client.GetFromJsonAsync<TelemetrySettingsJson>(
            telemetryPath + "/settings");
        Assert.Equal(0, settings!.Version);
        settings = await SendAsync<TelemetrySettingsJson>(client.PutAsJsonAsync(
            telemetryPath + "/settings",
            new
            {
                samplingRatio = 0.25,
                tracesEnabled = true,
                metricsEnabled = true,
                logsEnabled = false,
                exporterEndpoint = "http://collector.internal:4318",
                exporterProtocol = "OTLP_PROTOCOL_HTTP_PROTOBUF",
                diagnosticsBaseUrl = "https://observability.example/traces",
                expectedVersion = settings.Version,
            }));
        Assert.Equal(0.25, settings.SamplingRatio);
        Assert.Equal("OTLP_PROTOCOL_HTTP_PROTOBUF", settings.ExporterProtocol);

        const string traceId = "0123456789abcdef0123456789abcdef";
        using (var serviceScope = _factory.Services.CreateScope())
        {
            var store = serviceScope.ServiceProvider.GetRequiredService<ITelemetryStore>();
            await store.AppendErrorAsync(
                new TelemetryError(
                    Guid.CreateVersion7(),
                    new TelemetryScope(
                        Guid.Parse(scope.TenantId),
                        Guid.Parse(scope.ApplicationId),
                        Guid.Parse(scope.EnvironmentId)),
                    "Asterloom.Server",
                    "System.InvalidOperationException",
                    "Contract diagnostic",
                    "/asterloom.telemetry.contract/Test",
                    traceId,
                    "0123456789abcdef",
                    "telemetry-contract-request",
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        var health = await client.GetFromJsonAsync<CollectorHealthJson>(
            telemetryPath + "/collector/health");
        Assert.Contains(health!.Status, CollectorStatuses);
        var errors = await client.GetFromJsonAsync<TelemetryErrorListJson>(
            telemetryPath + $"/errors?pageSize=20&traceId={traceId}");
        Assert.Contains(errors!.Errors, item => item.TraceId == traceId);

        var diagnostic = await SendAsync<DiagnosticLinkJson>(client.PostAsJsonAsync(
            telemetryPath + ":diagnostic-link",
            new { traceId }));
        Assert.Contains(traceId, diagnostic.Url, StringComparison.Ordinal);
        Assert.Equal(traceId, diagnostic.TraceId);
    }

    private static async Task<ScopeResources> CreateScopeAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            "/api/v1/tenants",
            new { slug = $"telemetry-{suffix}", displayName = "Telemetry Tenant" }));
        var application = await SendAsync<ResourceJson>(client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/applications",
            new { slug = $"observed-{suffix}", displayName = "Telemetry App" }));
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
        const string clientId = "telemetry-contract-tests";
        const string clientSecret = "Telemetry-Contract-Tests-Secret!2026";
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
                    DisplayName = "Telemetry contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }

            var store = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
            var bindingId = Guid.Parse("cdcdcdcd-cdcd-7dcd-8dcd-cdcdcdcdcdcd");
            if (await store.GetRoleBindingAsync(bindingId, CancellationToken.None) is null)
            {
                var management = scope.ServiceProvider.GetRequiredService<AuthorizationManagementService>();
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

    private sealed record ScopeResources(string TenantId, string ApplicationId, string EnvironmentId);
    private sealed record ResourceJson(string Id);
    private sealed record TelemetrySourceJson(
        string Id,
        string DisplayName,
        string Description,
        string ResourceAttributesJson,
        string Status,
        long Version);
    private sealed record TelemetrySourceListJson(IReadOnlyList<TelemetrySourceJson> Sources);
    private sealed record TelemetrySettingsJson(
        double SamplingRatio,
        string ExporterProtocol,
        long Version);
    private sealed record CollectorHealthJson(string Status);
    private sealed record TelemetryErrorJson(string TraceId);
    private sealed record TelemetryErrorListJson(IReadOnlyList<TelemetryErrorJson> Errors);
    private sealed record DiagnosticLinkJson(string Url, string TraceId);
}
