using Asterloom.Sdk.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Asterloom.ContractTests;

public sealed class IdentityServiceCredentialSdkContractTests
{
    [Fact]
    public async Task ServiceCredentialSdkDiscoversPassportAndCachesAccessToken()
    {
        var issuer = new Uri("http://localhost:5080/");
        using var factory = new WebApplicationFactory<Program>();
        factory.UseKestrel(issuer.Port);
        factory.StartServer();
        var addresses = factory.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        Assert.Contains(
            addresses,
            address => new Uri(address).Port == issuer.Port);
        using var serverClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = issuer,
            AllowAutoRedirect = false,
        });
        using (var response = await serverClient.GetAsync(
            ".well-known/openid-configuration"))
        {
            response.EnsureSuccessStatusCode();
        }

        var clientId = "identity-sdk-service-" + Guid.NewGuid().ToString("N");
        const string clientSecret = "Identity-Sdk-Service-Contract!2026";
        using (var scope = factory.Services.CreateScope())
        {
            var applications = scope.ServiceProvider
                .GetRequiredService<IOpenIddictApplicationManager>();
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ApplicationType = ApplicationTypes.Web,
                ClientId = clientId,
                ClientSecret = clientSecret,
                ClientType = ClientTypes.Confidential,
                DisplayName = "Identity SDK service credential contract",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + "asterloom.api",
                },
            });
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAsterloomIdentityClient(options =>
        {
            options.Issuer = issuer;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.EnableServiceCredentials = true;
            options.AllowInsecureHttpForDevelopment = true;
        });
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var sdk = host.Services.GetRequiredService<AsterloomIdentityClient>();
            var first = await sdk.GetServiceAccessTokenAsync();
            var cached = await sdk.GetServiceAccessTokenAsync();
            var refreshed = await sdk.GetServiceAccessTokenAsync(forceRefresh: true);

            Assert.False(string.IsNullOrWhiteSpace(first));
            Assert.Equal(first, cached);
            Assert.NotEqual(first, refreshed);
            var stored = await sdk.GetStoredTokensAsync();
            Assert.Equal(refreshed, stored!.AccessToken);
            Assert.NotNull(stored.AccessTokenExpiresAt);
        }
        finally
        {
            await host.StopAsync();
        }
    }

}
