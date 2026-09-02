using Asterloom.Sdk.Identity.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class IdentityResourceServerSdkTests
{
    [Fact]
    public void RegistrationRejectsUnsafeOrIncompleteResourceServers()
    {
        var insecure = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => insecure.AddAsterloomResourceServer(options =>
        {
            options.Issuer = new Uri("http://identity.example.test/");
            options.Audience = "business-api";
            options.AllowInsecureHttpForDevelopment = true;
        }));

        var incompleteBinding = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            incompleteBinding.AddAsterloomResourceServer(options =>
            {
                options.Issuer = new Uri("https://identity.example.test/");
                options.Audience = "business-api";
                options.ApplicationId = Guid.NewGuid();
            }));
    }

    [Fact]
    public void RegistrationAddsJwtAuthenticationAndPermissionRequirement()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAsterloomResourceServer(options =>
        {
            options.Issuer = new Uri("https://identity.example.test/");
            options.Audience = "business-api";
        });
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAsterloomPermission(" orders.read ")
            .Build();

        var requirement = Assert.Single(
            policy.Requirements.OfType<AsterloomPermissionRequirement>());
        Assert.Equal("orders.read", requirement.Permission);
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IAuthenticationSchemeProvider>());
        Assert.Contains(
            provider.GetServices<IAuthorizationHandler>(),
            static handler => handler.GetType().Name
                == "AsterloomPermissionAuthorizationHandler");
    }
}
