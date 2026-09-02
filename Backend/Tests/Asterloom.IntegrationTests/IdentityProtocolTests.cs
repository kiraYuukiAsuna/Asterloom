using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asterloom.Modules.Identity;
using Asterloom.Modules.Identity.Bootstrap;
using Asterloom.Sdk.Identity.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
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
        Assert.DoesNotContain(GrantTypes.Password, grants);
        Assert.DoesNotContain(GrantTypes.Implicit, grants);
        Assert.Equal(
            [CodeChallengeMethods.Sha256],
            document.GetProperty("code_challenge_methods_supported")
                .EnumerateArray()
                .Select(static item => item.GetString()));
    }

    [Fact]
    public async Task AuthorizationEndpointRejectsPlainPkce()
    {
        var authorizePath = QueryHelpers.AddQueryString(
            "/connect/authorize",
            new Dictionary<string, string?>
            {
                [Parameters.ClientId] = ClientId,
                [Parameters.RedirectUri] = RedirectUri,
                [Parameters.ResponseType] = ResponseTypes.Code,
                [Parameters.Scope] = "openid asterloom.api",
                [Parameters.CodeChallenge] =
                    "asterloom-plain-verifier-0123456789-ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                [Parameters.CodeChallengeMethod] = CodeChallengeMethods.Plain,
            });

        using var response = await _client.GetAsync(authorizePath);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            $"{Parameters.Error}:{Errors.InvalidRequest}",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenEndpointRejectsPasswordGrant()
    {
        using var response = await PostTokenAsync(
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.Password,
                [Parameters.ClientId] = ClientId,
                [Parameters.Username] = AdminEmail,
                [Parameters.Password] = AdminPassword,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            Errors.UnsupportedGrantType,
            document.GetProperty(Parameters.Error).GetString());
    }

    [Fact]
    public async Task BootstrapRemovesLegacyPasswordGrantPermissions()
    {
        var clientIds = Enumerable.Range(0, 2)
            .Select(_ => "legacy-password-" + Guid.NewGuid().ToString("N"))
            .ToArray();
        using var scope = _factory.Services.CreateScope();
        var applications = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var clientId in clientIds)
        {
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = "legacy-test-secret",
                ClientType = ClientTypes.Confidential,
                DisplayName = "Legacy password client",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.Password,
                },
            });
        }

        await scope.ServiceProvider.GetRequiredService<IIdentityBootstrapper>()
            .BootstrapAsync(CancellationToken.None);

        foreach (var clientId in clientIds)
        {
            var application = await applications.FindByClientIdAsync(clientId)
                ?? throw new InvalidOperationException(
                    "The legacy test client was not found.");
            Assert.DoesNotContain(
                Permissions.GrantTypes.Password,
                await applications.GetPermissionsAsync(application));
        }
    }

    [Fact]
    public async Task PassportRememberMeControlsCookiePersistence()
    {
        foreach (var rememberMe in new[] { false, true })
        {
            using var client = _factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false,
                    HandleCookies = true,
                });
            using var loginPage = await client.GetAsync("/passport/login?returnUrl=%2F");
            loginPage.EnsureSuccessStatusCode();
            var loginHtml = await loginPage.Content.ReadAsStringAsync();
            var antiforgeryToken = WebUtility.HtmlDecode(
                AntiforgeryTokenPattern().Match(loginHtml).Groups[1].Value);

            using var loginContent = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Email"] = AdminEmail,
                    ["Password"] = AdminPassword,
                    ["RememberMe"] = rememberMe.ToString(),
                    ["ReturnUrl"] = "/",
                    ["__RequestVerificationToken"] = antiforgeryToken,
                });
            using var loginResponse = await client.PostAsync(
                "/passport/login",
                loginContent);
            loginResponse.EnsureSuccessStatusCode();
            var passportCookie = Assert.Single(
                loginResponse.Headers.GetValues("Set-Cookie"),
                static value => value.StartsWith(
                        "Asterloom.Passport.Development=",
                        StringComparison.Ordinal));

            Assert.Equal(
                rememberMe,
                passportCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));
            if (rememberMe)
            {
                var expiresAt = DateTimeOffset.Parse(
                    CookieExpiresPattern().Match(passportCookie).Groups[1].Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal);
                Assert.InRange(
                    expiresAt - DateTimeOffset.UtcNow,
                    TimeSpan.FromDays(29),
                    TimeSpan.FromDays(31));
            }
        }
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
                ["RememberMe"] = "true",
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
        Assert.Equal(3, accessToken!.Split('.').Length);
        using var accessTokenHeader = JsonDocument.Parse(
            WebEncoders.Base64UrlDecode(accessToken.Split('.')[0]));
        Assert.Equal("at+jwt", accessTokenHeader.RootElement.GetProperty("typ").GetString());
        var accessTokenKeyId = accessTokenHeader.RootElement.GetProperty("kid").GetString();
        using var discoveryResponse = await _client.GetAsync(
            "/.well-known/openid-configuration");
        discoveryResponse.EnsureSuccessStatusCode();
        var discovery = await discoveryResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var signingKeysResponse = await _client.GetAsync(
            new Uri(discovery.GetProperty("jwks_uri").GetString()!).PathAndQuery);
        signingKeysResponse.EnsureSuccessStatusCode();
        var signingKeys = await signingKeysResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            signingKeys.GetProperty("keys").EnumerateArray(),
            key => string.Equals(
                key.GetProperty("kid").GetString(),
                accessTokenKeyId,
                StringComparison.Ordinal));
        using var accessTokenPayload = JsonDocument.Parse(
            WebEncoders.Base64UrlDecode(accessToken.Split('.')[1]));
        Assert.Contains(
            "asterloom-api",
            ReadStringOrArray(accessTokenPayload.RootElement.GetProperty("aud")));
        var idToken = tokens.GetProperty(Parameters.IdToken).GetString();
        Assert.False(string.IsNullOrWhiteSpace(idToken));
        using var idTokenPayload = JsonDocument.Parse(
            WebEncoders.Base64UrlDecode(idToken!.Split('.')[1]));
        Assert.Equal(
            bool.TrueString,
            idTokenPayload.RootElement
                .GetProperty(IdentityClaimTypes.PersistentSession)
                .GetString());

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

        await VerifyExternalResourceServerAsync(accessToken, idToken!);

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
        var properties = await manager.GetPropertiesAsync(application);
        Assert.True(properties["asterloom:configuration_managed"].GetBoolean());
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

    private async Task VerifyExternalResourceServerAsync(
        string accessToken,
        string identityToken)
    {
        var webKeys = new JsonWebKeySet(
            await _client.GetStringAsync("/.well-known/jwks"));
        var metadata = new OpenIdConnectConfiguration
        {
            Issuer = "http://localhost/",
        };
        foreach (var signingKey in webKeys.GetSigningKeys())
        {
            metadata.SigningKeys.Add(signingKey);
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAsterloomResourceServer(options =>
        {
            options.Issuer = new Uri("http://localhost/");
            options.Audience = "asterloom-api";
            options.AllowInsecureHttpForDevelopment = true;
        });
        builder.Services.PostConfigure<JwtBearerOptions>(
            AsterloomResourceServerDefaults.AuthenticationScheme,
            options =>
            {
                options.Configuration = metadata;
                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                options.BackchannelHttpHandler = _factory.Server.CreateHandler();
                options.Backchannel = new HttpClient(options.BackchannelHttpHandler);
            });
        builder.Services
            .AddHttpClient(AsterloomResourceServerDefaults.AuthorizationHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(_factory.Server.CreateHandler);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(
                "platform-read",
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireAsterloomPermission("platform.info.read"));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet(
                "/protected",
                (ClaimsPrincipal principal) => principal.FindFirstValue(Claims.Subject))
            .RequireAuthorization();
        app.MapGet("/permission", () => "allowed")
            .RequireAuthorization("platform-read");
        await app.StartAsync();

        using var resourceClient = app.GetTestClient();
        resourceClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        using var protectedResponse = await resourceClient.GetAsync("/protected");
        Assert.True(
            protectedResponse.IsSuccessStatusCode,
            $"Resource server returned {(int)protectedResponse.StatusCode}: "
            + string.Join(", ", protectedResponse.Headers.WwwAuthenticate));
        Assert.False(string.IsNullOrWhiteSpace(
            await protectedResponse.Content.ReadAsStringAsync()));
        using var permissionResponse = await resourceClient.GetAsync("/permission");
        permissionResponse.EnsureSuccessStatusCode();

        resourceClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", identityToken);
        using var identityTokenResponse = await resourceClient.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, identityTokenResponse.StatusCode);
    }

    private static IEnumerable<string> ReadStringOrArray(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(static item => item.GetString() ?? string.Empty)
            : [value.GetString() ?? string.Empty];

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

    [GeneratedRegex("expires=([^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CookieExpiresPattern();

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
