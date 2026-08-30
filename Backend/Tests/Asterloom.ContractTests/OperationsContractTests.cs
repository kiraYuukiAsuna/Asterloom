using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class OperationsContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OperationsContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task JsonTranscodingPublishesCatalogHealthAndCanonicalOpenApi()
    {
        using var client = await CreateAuthorizedClientAsync();
        var catalog = await client.GetFromJsonAsync<ApiListJson>(
            "/api/v1/operations/apis?category=admin");
        Assert.NotNull(catalog);
        Assert.Contains(
            catalog.Apis,
            item => item.Service == "asterloom.operations.admin.v1.OperationsAdminService"
                && item.Rpc == "ListApis"
                && item.HttpMethod == "GET"
                && item.HttpPath == "/api/v1/operations/apis");
        Assert.All(catalog.Apis, static item =>
        {
            Assert.NotEmpty(item.HttpMethod);
            Assert.StartsWith("/", item.HttpPath, StringComparison.Ordinal);
        });

        var health = await client.GetFromJsonAsync<OperationsHealthJson>(
            "/api/v1/operations/health");
        Assert.Equal("DEPENDENCY_HEALTH_STATUS_HEALTHY", health!.Status);
        Assert.Contains(
            health.Dependencies,
            dependency => dependency.Name == "self"
                && dependency.Status == "DEPENDENCY_HEALTH_STATUS_HEALTHY");

        var document = await client.GetFromJsonAsync<OpenApiDocumentJson>(
            "/api/v1/operations/openapi");
        Assert.NotNull(document);
        Assert.Equal(64, document.Sha256.Length);
        using var parsed = JsonDocument.Parse(document.Content);
        Assert.True(parsed.RootElement.GetProperty("paths").TryGetProperty(
            "/api/v1/operations/apis",
            out _));
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        const string clientId = "operations-contract-tests";
        const string clientSecret = "Operations-Contract-Tests-Secret!2026";
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
                    DisplayName = "Operations contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }

            var store = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
            var bindingId = Guid.Parse("dededede-dede-7ede-8ede-dededededede");
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

    private sealed record ApiJson(
        string Service,
        string Rpc,
        string HttpMethod,
        string HttpPath);
    private sealed record ApiListJson(IReadOnlyList<ApiJson> Apis);
    private sealed record DependencyJson(string Name, string Status);
    private sealed record OperationsHealthJson(
        string Status,
        IReadOnlyList<DependencyJson> Dependencies);
    private sealed record OpenApiDocumentJson(string Content, string Sha256);
}
