using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Sdk.Identity;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed partial class IdentityAdminContractTests(
    WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] InitialScopeResources = ["contract-api"];
    private static readonly string[] UpdatedScopeResources =
        ["contract-api", "contract-worker"];
    private static readonly string[] ClientCredentialsGrant =
        ["OIDC_GRANT_TYPE_CLIENT_CREDENTIALS"];
    private static readonly string[] ViewerRole = ["Viewer"];
    private static readonly string[] DeveloperViewerRoles = ["Developer", "Viewer"];
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory =
        factory.WithWebHostBuilder(_ => { });

    [Fact]
    public async Task JsonTranscodingManagesCompleteIdentitySurface()
    {
        using var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");

        var scope = await SendAsync<ScopeJson>(client.PostAsJsonAsync(
            "/api/v1/identity/scopes",
            new
            {
                name = "contract." + suffix,
                displayName = "Contract scope",
                description = "Initial scope",
                resources = InitialScopeResources,
            }));
        var fetchedScope = await client.GetFromJsonAsync<ScopeJson>(
            $"/api/v1/identity/scopes/{scope.Id}");
        Assert.Equal(scope.Name, fetchedScope!.Name);
        scope = await SendAsync<ScopeJson>(client.PatchAsJsonAsync(
            $"/api/v1/identity/scopes/{scope.Id}",
            new
            {
                displayName = "Updated contract scope",
                description = "Updated scope",
                resources = UpdatedScopeResources,
                expectedVersion = scope.Version,
            }));

        var credential = await SendAsync<ClientCredentialJson>(client.PostAsJsonAsync(
            "/api/v1/identity/clients",
            new
            {
                clientId = "contract-client-" + suffix,
                displayName = "Contract service client",
                applicationType = "OIDC_APPLICATION_TYPE_WEB",
                clientType = "OIDC_CLIENT_TYPE_CONFIDENTIAL",
                grantTypes = ClientCredentialsGrant,
                scopes = new[] { "asterloom.api", scope.Name },
            }));
        Assert.False(string.IsNullOrWhiteSpace(credential.ClientSecret));
        Assert.Equal("OIDC_APPLICATION_TYPE_WEB", credential.Client.ApplicationType);
        var originalSecret = credential.ClientSecret;
        var oidcClient = await client.GetFromJsonAsync<ClientJson>(
            $"/api/v1/identity/clients/{credential.Client.ClientId}");
        Assert.Equal(credential.Client.Id, oidcClient!.Id);
        oidcClient = await SendAsync<ClientJson>(client.PatchAsJsonAsync(
            $"/api/v1/identity/clients/{oidcClient.ClientId}",
            new
            {
                displayName = "Updated contract service client",
                grantTypes = ClientCredentialsGrant,
                scopes = new[] { "asterloom.api", scope.Name },
                expectedVersion = oidcClient.Version,
            }));
        using (var originalToken = await RequestClientTokenAsync(
            oidcClient.ClientId,
            originalSecret))
        {
            originalToken.EnsureSuccessStatusCode();
        }

        credential = await SendAsync<ClientCredentialJson>(client.PostAsJsonAsync(
            $"/api/v1/identity/clients/{oidcClient.ClientId}:rotate-secret",
            new { expectedVersion = oidcClient.Version }));
        Assert.NotEqual(originalSecret, credential.ClientSecret);
        using (var staleToken = await RequestClientTokenAsync(
            credential.Client.ClientId,
            originalSecret))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, staleToken.StatusCode);
        }

        using (var rotatedToken = await RequestClientTokenAsync(
            credential.Client.ClientId,
            credential.ClientSecret))
        {
            rotatedToken.EnsureSuccessStatusCode();
        }

        using var scopeInUse = await client.DeleteAsync(
            $"/api/v1/identity/scopes/{scope.Id}?expectedVersion={scope.Version}");
        Assert.Equal(HttpStatusCode.BadRequest, scopeInUse.StatusCode);
        oidcClient = await SendAsync<ClientJson>(client.DeleteAsync(
            $"/api/v1/identity/clients/{credential.Client.ClientId}" +
            $"?expectedVersion={credential.Client.Version}"));
        Assert.Equal(credential.Client.ClientId, oidcClient.ClientId);
        scope = await SendAsync<ScopeJson>(client.DeleteAsync(
            $"/api/v1/identity/scopes/{scope.Id}?expectedVersion={scope.Version}"));
        Assert.Equal("Updated contract scope", scope.DisplayName);

        var invitation = await SendAsync<InvitationJson>(client.PostAsJsonAsync(
            "/api/v1/identity/users:invite",
            new
            {
                email = $"contract-{suffix}@asterloom.test",
                displayName = "Contract user",
                roles = ViewerRole,
            }));
        Assert.Equal("IDENTITY_USER_STATUS_PENDING", invitation.User.Status);
        invitation = await SendAsync<InvitationJson>(client.PostAsJsonAsync(
            $"/api/v1/identity/users/{invitation.User.Id}:resend-invitation",
            new { expectedVersion = invitation.User.Version }));
        await AcceptInvitationAsync(invitation.InvitationUrl);

        var user = await client.GetFromJsonAsync<UserJson>(
            $"/api/v1/identity/users/{invitation.User.Id}");
        Assert.Equal("IDENTITY_USER_STATUS_ACTIVE", user!.Status);
        user = await SendAsync<UserJson>(client.PatchAsJsonAsync(
            $"/api/v1/identity/users/{user.Id}",
            new { displayName = "Updated contract user", expectedVersion = user.Version }));
        user = await SendAsync<UserJson>(client.PutAsJsonAsync(
            $"/api/v1/identity/users/{user.Id}/roles",
            new { roles = DeveloperViewerRoles, expectedVersion = user.Version }));
        Assert.Equal(DeveloperViewerRoles, user.Roles);

        var sessionId = await CreateAuthorizationAsync(user.Id);
        var sessions = await client.GetFromJsonAsync<SessionListJson>(
            $"/api/v1/identity/users/{user.Id}/sessions");
        Assert.Contains(sessions!.Sessions, session => session.Id == sessionId);
        var revoked = await SendAsync<SessionJson>(client.DeleteAsync(
            $"/api/v1/identity/users/{user.Id}/sessions/{sessionId}"));
        Assert.Equal("IDENTITY_SESSION_STATUS_REVOKED", revoked.Status);
        sessions = await client.GetFromJsonAsync<SessionListJson>(
            $"/api/v1/identity/users/{user.Id}/sessions?includeRevoked=true");
        Assert.Contains(
            sessions!.Sessions,
            session => session.Id == sessionId
                && session.Status == "IDENTITY_SESSION_STATUS_REVOKED");

        await CreateAuthorizationAsync(user.Id);
        var revokedAll = await SendAsync<RevokeAllJson>(client.PostAsJsonAsync(
            $"/api/v1/identity/users/{user.Id}/sessions:revoke-all",
            new { }));
        Assert.True(revokedAll.RevokedSessions >= 1);

        user = await SendAsync<UserJson>(client.PostAsJsonAsync(
            $"/api/v1/identity/users/{user.Id}:suspend",
            new { expectedVersion = user.Version }));
        Assert.Equal("IDENTITY_USER_STATUS_SUSPENDED", user.Status);
        user = await SendAsync<UserJson>(client.PostAsJsonAsync(
            $"/api/v1/identity/users/{user.Id}:reactivate",
            new { expectedVersion = user.Version }));
        user = await SendAsync<UserJson>(client.DeleteAsync(
            $"/api/v1/identity/users/{user.Id}?expectedVersion={user.Version}"));
        Assert.Equal("IDENTITY_USER_STATUS_ARCHIVED", user.Status);
        user = await SendAsync<UserJson>(client.PostAsJsonAsync(
            $"/api/v1/identity/users/{user.Id}:restore",
            new { expectedVersion = user.Version }));
        Assert.Equal("IDENTITY_USER_STATUS_ACTIVE", user.Status);

        var users = await client.GetFromJsonAsync<UserListJson>(
            $"/api/v1/identity/users?query={Uri.EscapeDataString(user.Email)}&includeArchived=true");
        var clients = await client.GetFromJsonAsync<ClientListJson>(
            "/api/v1/identity/clients?pageSize=100");
        var scopes = await client.GetFromJsonAsync<ScopeListJson>(
            "/api/v1/identity/scopes?pageSize=100");
        Assert.Contains(users!.Users, item => item.Id == user.Id);
        Assert.Contains(clients!.Clients, item => item.ClientId == "asterloom-web");
        Assert.Contains(scopes!.Scopes, item => item.Name == "asterloom.api");
    }

    [Fact]
    public async Task IdentityAdminSdkPersistsNativePkceApplicationType()
    {
        using var httpClient = await CreateAuthorizedClientAsync();
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient });
        var sdk = new AsterloomIdentityAdminClient(channel.CreateCallInvoker());
        var clientId = "sdk-native-" + Guid.NewGuid().ToString("N");
        var credential = await sdk.CreateClientAsync(new(
            clientId,
            "SDK native contract client",
            AsterloomOidcApplicationType.Native,
            AsterloomOidcClientType.Public,
            [
                AsterloomOidcGrantType.AuthorizationCode,
                AsterloomOidcGrantType.RefreshToken,
            ],
            [new Uri("http://localhost/")],
            [new Uri("com.asterloom.contract:/logout")],
            ["asterloom.api"]));

        Assert.Empty(credential.ClientSecret);
        Assert.Equal(AsterloomOidcApplicationType.Native, credential.Client.ApplicationType);
        Assert.Equal(AsterloomOidcClientType.Public, credential.Client.ClientType);
        Assert.Contains(
            AsterloomOidcGrantType.AuthorizationCode,
            credential.Client.GrantTypes);
        var fetched = await sdk.GetClientAsync(clientId);
        Assert.Equal(credential.Client.Id, fetched.Id);
        Assert.Contains(new Uri("http://localhost/"), fetched.RedirectUris);
        var listed = await sdk.ListClientsAsync(clientId);
        Assert.Contains(listed.Items, item => item.ClientId == clientId);

        using (var scope = _factory.Services.CreateScope())
        {
            var applications = scope.ServiceProvider
                .GetRequiredService<IOpenIddictApplicationManager>();
            var application = await applications.FindByClientIdAsync(clientId)
                ?? throw new InvalidOperationException("The native client was not persisted.");
            Assert.Equal(
                ApplicationTypes.Native,
                await applications.GetApplicationTypeAsync(application));
        }

        fetched = await sdk.UpdateClientAsync(fetched, new(
            "Updated SDK native contract client",
            [
                AsterloomOidcGrantType.AuthorizationCode,
                AsterloomOidcGrantType.RefreshToken,
            ],
            [new Uri("http://127.0.0.1/")],
            [new Uri("com.asterloom.contract:/signed-out")],
            ["asterloom.api"]));
        Assert.Equal("Updated SDK native contract client", fetched.DisplayName);
        Assert.Contains(new Uri("http://127.0.0.1/"), fetched.RedirectUris);

        await Assert.ThrowsAsync<ArgumentException>(() => sdk.CreateClientAsync(new(
            "invalid-native-" + Guid.NewGuid().ToString("N"),
            "Invalid native client",
            AsterloomOidcApplicationType.Native,
            AsterloomOidcClientType.Confidential,
            [AsterloomOidcGrantType.AuthorizationCode],
            [new Uri("http://localhost/")],
            [],
            ["asterloom.api"])));

        var deleted = await sdk.DeleteClientAsync(fetched);
        Assert.Equal(clientId, deleted.ClientId);
        using (var scope = _factory.Services.CreateScope())
        {
            var applications = scope.ServiceProvider
                .GetRequiredService<IOpenIddictApplicationManager>();
            Assert.Null(await applications.FindByClientIdAsync(clientId));
        }
    }

    [Fact]
    public async Task ReadingLoginPageDoesNotConsumePasswordAttemptQuota()
    {
        using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        for (var index = 0; index < 12; index++)
        {
            using var response = await browser.GetAsync("/passport/login");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private async Task AcceptInvitationAsync(string invitationUrl)
    {
        using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var invitationUri = new Uri(invitationUrl);
        using var pageResponse = await browser.GetAsync(invitationUri.PathAndQuery);
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync();
        var antiforgery = WebUtility.HtmlDecode(
            AntiforgeryTokenPattern().Match(html).Groups[1].Value);
        var userId = HiddenValuePattern("UserId").Match(html).Groups[1].Value;
        var token = WebUtility.HtmlDecode(
            HiddenValuePattern("Token").Match(html).Groups[1].Value);
        Assert.False(string.IsNullOrWhiteSpace(antiforgery));
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var response = await browser.PostAsync(
            "/passport/invitation",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UserId"] = userId,
                ["Token"] = token,
                ["Password"] = "Contract-User-Password!2026",
                ["ConfirmPassword"] = "Contract-User-Password!2026",
                ["__RequestVerificationToken"] = antiforgery,
            }));
        response.EnsureSuccessStatusCode();
        Assert.Contains("账户已激活", await response.Content.ReadAsStringAsync());
    }

    private async Task<string> CreateAuthorizationAsync(string userId)
    {
        using var serviceScope = _factory.Services.CreateScope();
        var applications = serviceScope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = serviceScope.ServiceProvider
            .GetRequiredService<IOpenIddictAuthorizationManager>();
        var application = await applications.FindByClientIdAsync("asterloom-web")
            ?? throw new InvalidOperationException("The bootstrap Web client is missing.");
        var applicationId = await applications.GetIdAsync(application)
            ?? throw new InvalidOperationException("The bootstrap Web client has no ID.");
        var authorization = await authorizations.CreateAsync(
            new ClaimsIdentity(),
            userId,
            applicationId,
            AuthorizationTypes.Permanent,
            ImmutableArray.Create(Scopes.OpenId, "asterloom.api"));
        return await authorizations.GetIdAsync(authorization)
            ?? throw new InvalidOperationException("The authorization has no ID.");
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        const string clientId = "identity-admin-contract-tests";
        const string clientSecret = "Identity-Admin-Contract-Tests!2026";
        using (var scope = _factory.Services.CreateScope())
        {
            var applications = scope.ServiceProvider
                .GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync(clientId) is null)
            {
                await applications.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    ClientType = ClientTypes.Confidential,
                    DisplayName = "Identity admin contract tests",
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "asterloom.api",
                    },
                });
            }

            var store = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
            var bindingId = Guid.Parse("bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb");
            if (await store.GetRoleBindingAsync(bindingId, CancellationToken.None) is null)
            {
                var management = scope.ServiceProvider
                    .GetRequiredService<AuthorizationManagementService>();
                var role = AuthorizationCatalog.FindSystemRole("super-administrator")!;
                await management.SetRoleBindingAsync(
                    bindingId.ToString("D"),
                    clientId,
                    role.Id.ToString("D"),
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

    private async Task<HttpResponseMessage> RequestClientTokenAsync(
        string clientId,
        string clientSecret)
    {
        using var client = _factory.CreateClient();
        return await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.ClientCredentials,
                [Parameters.ClientId] = clientId,
                [Parameters.ClientSecret] = clientSecret,
                [Parameters.Scope] = "asterloom.api",
            }));
    }

    private static async Task<T> SendAsync<T>(Task<HttpResponseMessage> responseTask)
    {
        using var response = await responseTask;
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected success, received {response.StatusCode}: {content}");
        return JsonSerializer.Deserialize<T>(content, JsonOptions)!;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenPattern();

    private static Regex HiddenValuePattern(string name) => new(
        $"name=\"{Regex.Escape(name)}\" value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant);

    private sealed record UserJson(
        string Id,
        string Email,
        string DisplayName,
        string Status,
        long Version,
        IReadOnlyList<string> Roles);

    private sealed record UserListJson(IReadOnlyList<UserJson> Users);

    private sealed record InvitationJson(
        UserJson User,
        string InvitationUrl,
        DateTimeOffset ExpiresAt);

    private sealed record SessionJson(string Id, string Status);

    private sealed record SessionListJson(IReadOnlyList<SessionJson> Sessions);

    private sealed record RevokeAllJson(long RevokedSessions);

    private sealed record ClientJson(
        string Id,
        string ClientId,
        string DisplayName,
        string ApplicationType,
        string ClientType,
        IReadOnlyList<string> GrantTypes,
        IReadOnlyList<string> Scopes,
        string Version);

    private sealed record ClientCredentialJson(ClientJson Client, string ClientSecret);

    private sealed record ClientListJson(IReadOnlyList<ClientJson> Clients);

    private sealed record ScopeJson(
        string Id,
        string Name,
        string DisplayName,
        string Description,
        IReadOnlyList<string> Resources,
        string Version);

    private sealed record ScopeListJson(IReadOnlyList<ScopeJson> Scopes);
}
