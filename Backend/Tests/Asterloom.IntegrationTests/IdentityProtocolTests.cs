using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asterloom.Modules.Identity.Bootstrap;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.IntegrationTests;

public sealed partial class IdentityProtocolTests
    : IClassFixture<IdentityProtocolTests.IdentityWebApplicationFactory>
{
    private const string AdminEmail = "admin@asterloom.test";
    private const string AdminPassword = "Asterloom-Test-Admin!2026";
    private const string ClientId = "asterloom-web-test";
    private const string RedirectUri = "http://localhost/callback";

    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IdentityProtocolTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
    }

    [Fact]
    public async Task DiscoveryAdvertisesOnlyApprovedFlows()
    {
        using var response = await _client.GetAsync(
            "/.well-known/openid-configuration");
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("http://localhost/", document.GetProperty("issuer").GetString());
        Assert.Equal(
            "http://localhost/connect/authorize",
            document.GetProperty("authorization_endpoint").GetString());
        Assert.Equal(
            "http://localhost/connect/token",
            document.GetProperty("token_endpoint").GetString());
        var grants = document.GetProperty("grant_types_supported")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains(GrantTypes.AuthorizationCode, grants);
        Assert.Contains(GrantTypes.RefreshToken, grants);
        Assert.Contains(GrantTypes.ClientCredentials, grants);
        Assert.Contains(GrantTypes.Password, grants);
        Assert.DoesNotContain(GrantTypes.Implicit, grants);
        Assert.Contains(
            CodeChallengeMethods.Sha256,
            document.GetProperty("code_challenge_methods_supported")
                .EnumerateArray()
                .Select(static item => item.GetString()));
    }

    [Fact]
    public async Task AuthorizationCodePkceLoginRefreshAndUserInfoWorkEndToEnd()
    {
        const string verifier =
            "asterloom-pkce-verifier-0123456789-ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var challenge = Base64Url(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizePath = QueryHelpers.AddQueryString(
            "/connect/authorize",
            new Dictionary<string, string?>
            {
                [Parameters.ClientId] = ClientId,
                [Parameters.RedirectUri] = RedirectUri,
                [Parameters.ResponseType] = ResponseTypes.Code,
                [Parameters.Scope] = "openid profile email roles offline_access asterloom.api",
                [Parameters.CodeChallenge] = challenge,
                [Parameters.CodeChallengeMethod] = CodeChallengeMethods.Sha256,
                [Parameters.State] = "state-identity-test",
                [Parameters.Nonce] = "nonce-identity-test",
            });

        using var challengeResponse = await _client.GetAsync(authorizePath);
        Assert.Equal(HttpStatusCode.Redirect, challengeResponse.StatusCode);
        var loginLocation = Assert.IsType<Uri>(challengeResponse.Headers.Location);
        Assert.Equal("/passport/login", loginLocation.AbsolutePath);

        using var loginPage = await _client.GetAsync(loginLocation);
        loginPage.EnsureSuccessStatusCode();
        Assert.Equal("DENY", loginPage.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains(
            "no-store",
            loginPage.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var antiforgeryToken = WebUtility.HtmlDecode(
            AntiforgeryTokenPattern().Match(loginHtml).Groups[1].Value);
        var returnUrl = WebUtility.HtmlDecode(
            ReturnUrlPattern().Match(loginHtml).Groups[1].Value);
        Assert.False(string.IsNullOrWhiteSpace(antiforgeryToken));
        Assert.StartsWith("/connect/authorize?", returnUrl, StringComparison.Ordinal);

        using var loginContent = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Email"] = AdminEmail,
                ["Password"] = AdminPassword,
                ["RememberMe"] = "false",
                ["ReturnUrl"] = returnUrl,
                ["__RequestVerificationToken"] = antiforgeryToken,
            });
        using var loginResponse = await _client.PostAsync("/passport/login", loginContent);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Contains(
            "http-equiv=\"refresh\"",
            await loginResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var authorizationResponse = await _client.GetAsync(returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, authorizationResponse.StatusCode);
        var callback = Assert.IsType<Uri>(authorizationResponse.Headers.Location);
        Assert.Equal(RedirectUri, callback.GetLeftPart(UriPartial.Path));
        var callbackQuery = QueryHelpers.ParseQuery(callback.Query);
        Assert.Equal("state-identity-test", callbackQuery[Parameters.State].Single());
        var code = callbackQuery[Parameters.Code].Single();
        Assert.False(string.IsNullOrWhiteSpace(code));

        using var tokenResponse = await PostTokenAsync(
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.AuthorizationCode,
                [Parameters.ClientId] = ClientId,
                [Parameters.Code] = code,
                [Parameters.RedirectUri] = RedirectUri,
                [Parameters.CodeVerifier] = verifier,
            });
        tokenResponse.EnsureSuccessStatusCode();
        var tokens = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokens.GetProperty(Parameters.AccessToken).GetString();
        var refreshToken = tokens.GetProperty(Parameters.RefreshToken).GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
        Assert.False(string.IsNullOrWhiteSpace(
            tokens.GetProperty(Parameters.IdToken).GetString()));

        using var userInfoRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/connect/userinfo");
        userInfoRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        using var userInfoResponse = await _client.SendAsync(userInfoRequest);
        userInfoResponse.EnsureSuccessStatusCode();
        var userInfo = await userInfoResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(AdminEmail, userInfo.GetProperty(Claims.Email).GetString());
        Assert.Equal(
            "Asterloom Test Administrator",
            userInfo.GetProperty(Claims.Name).GetString());
        Assert.Contains(
            "SuperAdministrator",
            userInfo.GetProperty(Claims.Role)
                .EnumerateArray()
                .Select(static item => item.GetString()));

        using var refreshResponse = await PostTokenAsync(
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.RefreshToken,
                [Parameters.ClientId] = ClientId,
                [Parameters.RefreshToken] = refreshToken!,
            });
        refreshResponse.EnsureSuccessStatusCode();
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rotatedRefreshToken = refreshed.GetProperty(Parameters.RefreshToken).GetString();
        Assert.False(string.IsNullOrWhiteSpace(rotatedRefreshToken));
        Assert.NotEqual(refreshToken, rotatedRefreshToken);

        using var reusedResponse = await PostTokenAsync(
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.RefreshToken,
                [Parameters.ClientId] = ClientId,
                [Parameters.RefreshToken] = refreshToken!,
            });
        Assert.Equal(HttpStatusCode.BadRequest, reusedResponse.StatusCode);
        var reuseError = await reusedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            Errors.InvalidGrant,
            reuseError.GetProperty(Parameters.Error).GetString());
    }

    [Fact]
    public async Task ConfidentialServiceClientCanUseClientCredentials()
    {
        await EnsureServiceClientAsync();
        using var response = await PostTokenAsync(
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.ClientCredentials,
                [Parameters.ClientId] = "service-test",
                [Parameters.ClientSecret] = "Service-Test-Secret-2026!",
                [Parameters.Scope] = "asterloom.api",
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(
            payload.GetProperty(Parameters.AccessToken).GetString()));
        Assert.False(payload.TryGetProperty(Parameters.RefreshToken, out _));
    }

    [Fact]
    public async Task BootstrapReconcilesConfiguredWebClientRedirectUris()
    {
        using var isolatedFactory = new IdentityWebApplicationFactory();
        using var startupClient = isolatedFactory.CreateClient();
        using var scope = isolatedFactory.Services.CreateScope();
        var manager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();
        var bootstrapper = scope.ServiceProvider
            .GetRequiredService<IIdentityBootstrapper>();
        var application = await manager.FindByClientIdAsync(ClientId)
            ?? throw new InvalidOperationException("The bootstrap Web client is missing.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application);
        descriptor.RedirectUris.Clear();
        descriptor.RedirectUris.Add(new Uri("https://old.example.test/callback"));
        descriptor.PostLogoutRedirectUris.Clear();
        descriptor.PostLogoutRedirectUris.Add(
            new Uri("https://old.example.test/signed-out"));
        await manager.UpdateAsync(application, descriptor);

        await bootstrapper.BootstrapAsync(CancellationToken.None);

        application = await manager.FindByClientIdAsync(ClientId)
            ?? throw new InvalidOperationException("The reconciled Web client is missing.");
        Assert.Collection(
            await manager.GetRedirectUrisAsync(application),
            value => Assert.Equal(RedirectUri, value));
        Assert.Collection(
            await manager.GetPostLogoutRedirectUrisAsync(application),
            value => Assert.Equal("http://localhost/", value));
    }

    private async Task<HttpResponseMessage> PostTokenAsync(
        Dictionary<string, string> parameters)
    {
        using var content = new FormUrlEncodedContent(parameters);
        return await _client.PostAsync("/connect/token", content);
    }

    private async Task EnsureServiceClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();
        if (await manager.FindByClientIdAsync("service-test") is not null)
        {
            return;
        }

        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = "service-test",
            ClientSecret = "Service-Test-Secret-2026!",
            ClientType = ClientTypes.Confidential,
            DisplayName = "Integration test service",
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + "asterloom.api",
            },
        });
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenPattern();

    [GeneratedRegex("name=\"ReturnUrl\" value=\"([^\"]+)\"")]
    private static partial Regex ReturnUrlPattern();

    public sealed class IdentityWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Persistence:Provider", "Memory");
            builder.UseSetting("Identity:Issuer", "http://localhost/");
            builder.UseSetting("Identity:Bootstrap:AdminEmail", AdminEmail);
            builder.UseSetting("Identity:Bootstrap:AdminPassword", AdminPassword);
            builder.UseSetting(
                "Identity:Bootstrap:AdminDisplayName",
                "Asterloom Test Administrator");
            builder.UseSetting("Identity:WebClient:ClientId", ClientId);
            builder.UseSetting("Identity:WebClient:RedirectUri", RedirectUri);
            builder.UseSetting(
                "Identity:WebClient:PostLogoutRedirectUri",
                "http://localhost/");
        }
    }
}
