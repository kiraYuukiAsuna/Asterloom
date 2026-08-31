using System.Security.Claims;
using Asterloom.Sdk.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Asterloom.UnitTests;

public sealed class IdentitySdkTests
{
    [Fact]
    public async Task InteractiveSignInNormalizesHintAndStoresDomainTokens()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var store = new AsterloomInMemoryTokenStore();
        var protocol = new FakeIdentityProtocolClient
        {
            InteractiveResult = new(
                "interactive-access",
                clock.GetUtcNow().AddMinutes(5),
                "interactive-identity",
                "interactive-refresh",
                new ClaimsPrincipal(new ClaimsIdentity())),
        };
        using var client = CreateInteractiveClient(protocol, store, clock);

        var tokens = await client.SignInAsync("  person@asterloom.test  ");

        Assert.Equal("interactive-access", tokens.AccessToken);
        Assert.Equal("person@asterloom.test", protocol.LastLoginHint);
        Assert.Same(tokens, await store.ReadAsync());
    }

    [Fact]
    public async Task AccessTokenRefreshesInsideSkewAndPreservesUnrotatedTokens()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero));
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "oidc"));
        var store = new AsterloomInMemoryTokenStore();
        await store.WriteAsync(new(
            "access-1",
            clock.GetUtcNow().AddMinutes(5),
            "identity-1",
            "refresh-1",
            principal));
        var protocol = new FakeIdentityProtocolClient
        {
            RefreshResult = new(
                "access-2",
                clock.GetUtcNow().AddMinutes(15),
                IdentityToken: null,
                RefreshToken: null,
                Principal: null),
        };
        using var client = CreateInteractiveClient(protocol, store, clock);

        Assert.Equal("access-1", await client.GetAccessTokenAsync());
        Assert.Equal(0, protocol.RefreshCalls);

        clock.Advance(TimeSpan.FromMinutes(4.5));
        Assert.Equal("access-2", await client.GetAccessTokenAsync());
        Assert.Equal(1, protocol.RefreshCalls);
        var refreshed = await client.GetStoredTokensAsync();
        Assert.Equal("identity-1", refreshed!.IdentityToken);
        Assert.Equal("refresh-1", refreshed.RefreshToken);
        Assert.Same(principal, refreshed.Principal);
    }

    [Fact]
    public async Task ConcurrentServiceTokenRequestsUseOneProtocolCall()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero));
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var protocol = new FakeIdentityProtocolClient
        {
            ServiceHandler = async cancellationToken =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new(
                    "service-access",
                    clock.GetUtcNow().AddMinutes(10),
                    IdentityToken: null,
                    RefreshToken: null,
                    Principal: null);
            },
        };
        var options = CreateOptions();
        options.EnableServiceCredentials = true;
        options.ClientSecret = "unit-test-secret";
        using var client = new AsterloomIdentityClient(
            protocol,
            options,
            new AsterloomInMemoryTokenStore(),
            clock);

        var requests = Enumerable.Range(0, 8)
            .Select(_ => client.GetServiceAccessTokenAsync())
            .ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();
        var tokens = await Task.WhenAll(requests);

        Assert.All(tokens, token => Assert.Equal("service-access", token));
        Assert.Equal(1, protocol.ServiceCalls);
        Assert.Equal(["asterloom.api"], protocol.LastServiceScopes);
    }

    [Fact]
    public async Task PasswordAuthenticationNormalizesEmailAndDoesNotUseSharedTokenStore()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var store = new AsterloomInMemoryTokenStore();
        var protocol = new FakeIdentityProtocolClient
        {
            PasswordResult = new(
                "user-access",
                clock.GetUtcNow().AddMinutes(10),
                "user-identity",
                "user-refresh",
                new ClaimsPrincipal(new ClaimsIdentity())),
        };
        var options = CreateOptions();
        options.ClientSecret = "unit-test-secret";
        options.EnablePasswordAuthentication = true;
        using var client = new AsterloomIdentityClient(protocol, options, store, clock);

        var tokens = await client.AuthenticateWithPasswordAsync(
            "  person@asterloom.test  ",
            "correct horse battery staple");

        Assert.Equal("user-access", tokens.AccessToken);
        Assert.Equal("person@asterloom.test", protocol.LastPasswordUsername);
        Assert.Equal("correct horse battery staple", protocol.LastPassword);
        Assert.Equal(
            ["asterloom.api", "openid", "profile", "email", "roles", "offline_access"],
            protocol.LastPasswordScopes);
        Assert.Null(await store.ReadAsync());
    }

    [Fact]
    public async Task PasswordAuthenticationDoesNotSerializeIndependentUserSessions()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var protocol = new FakeIdentityProtocolClient
        {
            PasswordHandler = async cancellationToken =>
            {
                if (Interlocked.Increment(ref active) == 2)
                {
                    entered.TrySetResult();
                }

                await release.Task.WaitAsync(cancellationToken);
                Interlocked.Decrement(ref active);
                return new(
                    "user-access",
                    clock.GetUtcNow().AddMinutes(10),
                    "user-identity",
                    "user-refresh",
                    new ClaimsPrincipal(new ClaimsIdentity()));
            },
        };
        var options = CreateOptions();
        options.ClientSecret = "unit-test-secret";
        options.EnablePasswordAuthentication = true;
        using var client = new AsterloomIdentityClient(
            protocol,
            options,
            new AsterloomInMemoryTokenStore(),
            clock);

        var first = client.AuthenticateWithPasswordAsync("first@asterloom.test", "password");
        var second = client.AuthenticateWithPasswordAsync("second@asterloom.test", "password");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, protocol.PasswordCalls);
    }

    [Fact]
    public async Task UserTokenRefreshPreservesRotatedValuesWithoutUsingSharedStore()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var store = new AsterloomInMemoryTokenStore();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var protocol = new FakeIdentityProtocolClient
        {
            RefreshResult = new(
                "refreshed-user-access",
                clock.GetUtcNow().AddMinutes(10),
                IdentityToken: null,
                RefreshToken: null,
                Principal: null),
        };
        var options = CreateOptions();
        options.ClientSecret = "unit-test-secret";
        options.EnablePasswordAuthentication = true;
        using var client = new AsterloomIdentityClient(protocol, options, store, clock);
        var current = new AsterloomTokenSet(
            "old-user-access",
            clock.GetUtcNow().AddSeconds(-1),
            "user-identity",
            "user-refresh",
            principal);

        var refreshed = await client.RefreshUserTokensAsync(current);

        Assert.Equal("refreshed-user-access", refreshed.AccessToken);
        Assert.Equal("user-identity", refreshed.IdentityToken);
        Assert.Equal("user-refresh", refreshed.RefreshToken);
        Assert.Same(principal, refreshed.Principal);
        Assert.Null(await store.ReadAsync());
    }

    [Fact]
    public async Task ConcurrentExpiredUserTokenRequestsUseOneRefresh()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero));
        var store = new AsterloomInMemoryTokenStore();
        await store.WriteAsync(new(
            "expired-access",
            clock.GetUtcNow().AddSeconds(-1),
            "identity",
            "refresh",
            new ClaimsPrincipal(new ClaimsIdentity())));
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var protocol = new FakeIdentityProtocolClient
        {
            RefreshHandler = async cancellationToken =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new(
                    "refreshed-access",
                    clock.GetUtcNow().AddMinutes(10),
                    IdentityToken: null,
                    RefreshToken: null,
                    Principal: null);
            },
        };
        using var client = CreateInteractiveClient(protocol, store, clock);

        var requests = Enumerable.Range(0, 8)
            .Select(_ => client.GetAccessTokenAsync())
            .ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();
        var tokens = await Task.WhenAll(requests);

        Assert.All(tokens, token => Assert.Equal("refreshed-access", token));
        Assert.Equal(1, protocol.RefreshCalls);
    }

    [Fact]
    public async Task SignOutClearsLocalTokensWhenRemoteSignOutFails()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var store = new AsterloomInMemoryTokenStore();
        await store.WriteAsync(new(
            "access",
            clock.GetUtcNow().AddMinutes(5),
            "identity",
            "refresh",
            new ClaimsPrincipal(new ClaimsIdentity())));
        var protocol = new FakeIdentityProtocolClient
        {
            SignOutException = new InvalidOperationException("remote logout failed"),
        };
        using var client = CreateInteractiveClient(protocol, store, clock);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SignOutAsync());

        Assert.Equal("remote logout failed", exception.Message);
        Assert.Null(await store.ReadAsync());
        Assert.Equal("identity", protocol.LastIdentityTokenHint);
    }

    [Fact]
    public void RegistrationRejectsMixedInteractiveAndServiceModes()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddAsterloomIdentityClient(options =>
            {
                options.Issuer = new Uri("https://passport.asterloom.test/");
                options.ClientId = "mixed-client";
                options.ClientSecret = "secret";
                options.EnableInteractiveAuthentication = true;
                options.EnableServiceCredentials = true;
            }));

        Assert.Contains("separate public and confidential", exception.Message);
    }

    [Fact]
    public async Task InteractiveRegistrationStartsInsideGenericHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAsterloomIdentityClient(options =>
        {
            options.Issuer = new Uri("https://passport.asterloom.test/");
            options.ClientId = "native-desktop-client";
            options.EnableInteractiveAuthentication = true;
        });
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            Assert.NotNull(host.Services.GetRequiredService<AsterloomIdentityClient>());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public void SensitiveModelsRedactSecretsFromStringRepresentations()
    {
        var client = new AsterloomOidcClient(
            "id",
            "client",
            "Client",
            AsterloomOidcApplicationType.Web,
            AsterloomOidcClientType.Confidential,
            [AsterloomOidcGrantType.ClientCredentials],
            [],
            [],
            ["asterloom.api"],
            "version",
            TenantId: null,
            ApplicationId: null,
            AllowUserRegistration: false,
            AllowMembershipAutoJoin: false);
        var credential = new AsterloomOidcClientCredential(client, "top-secret");
        var tokens = new AsterloomTokenSet(
            "access-secret",
            null,
            "identity-secret",
            "refresh-secret",
            new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.DoesNotContain("top-secret", credential.ToString());
        Assert.DoesNotContain("access-secret", tokens.ToString());
        Assert.DoesNotContain("identity-secret", tokens.ToString());
        Assert.DoesNotContain("refresh-secret", tokens.ToString());
        Assert.Contains("[REDACTED]", credential.ToString());
        Assert.Contains("[REDACTED]", tokens.ToString());
    }

    private static AsterloomIdentityClient CreateInteractiveClient(
        IAsterloomIdentityProtocolClient protocol,
        IAsterloomTokenStore store,
        TimeProvider clock)
    {
        var options = CreateOptions();
        options.EnableInteractiveAuthentication = true;
        return new(protocol, options, store, clock);
    }

    private static AsterloomIdentityClientOptions CreateOptions() => new()
    {
        Issuer = new Uri("https://passport.asterloom.test/"),
        ClientId = "unit-test-client",
        RefreshBeforeExpiration = TimeSpan.FromMinutes(1),
    };

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow += value;
    }

    private sealed class FakeIdentityProtocolClient : IAsterloomIdentityProtocolClient
    {
        public AsterloomProtocolTokenResult? InteractiveResult { get; init; }

        public AsterloomProtocolTokenResult? RefreshResult { get; init; }

        public AsterloomProtocolTokenResult? PasswordResult { get; init; }

        public Func<CancellationToken, Task<AsterloomProtocolTokenResult>>?
            PasswordHandler { get; init; }

        public Func<CancellationToken, Task<AsterloomProtocolTokenResult>>?
            RefreshHandler { get; init; }

        public Func<CancellationToken, Task<AsterloomProtocolTokenResult>>?
            ServiceHandler { get; init; }

        public Exception? SignOutException { get; init; }

        public int RefreshCalls => Volatile.Read(ref _refreshCalls);

        public int ServiceCalls => Volatile.Read(ref _serviceCalls);

        public int PasswordCalls => Volatile.Read(ref _passwordCalls);

        public IReadOnlyList<string> LastServiceScopes { get; private set; } = [];

        public IReadOnlyList<string> LastPasswordScopes { get; private set; } = [];

        public string? LastPasswordUsername { get; private set; }

        public string? LastPassword { get; private set; }

        public string? LastIdentityTokenHint { get; private set; }

        public string? LastLoginHint { get; private set; }

        public Task<AsterloomProtocolTokenResult> AuthenticateInteractivelyAsync(
            string registrationId,
            string? loginHint,
            CancellationToken cancellationToken)
        {
            LastLoginHint = loginHint;
            return Task.FromResult(
                InteractiveResult
                ?? throw new InvalidOperationException("No interactive result configured."));
        }

        public Task<AsterloomProtocolTokenResult> AuthenticateWithRefreshTokenAsync(
            string registrationId,
            string refreshToken,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _refreshCalls);
            if (RefreshHandler is not null)
            {
                return RefreshHandler(cancellationToken);
            }

            return Task.FromResult(
                RefreshResult
                ?? throw new InvalidOperationException("No refresh result configured."));
        }

        public Task<AsterloomProtocolTokenResult> AuthenticateWithClientCredentialsAsync(
            string registrationId,
            IReadOnlyCollection<string> scopes,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _serviceCalls);
            LastServiceScopes = scopes.ToArray();
            return ServiceHandler?.Invoke(cancellationToken)
                ?? throw new InvalidOperationException("No service handler configured.");
        }

        public Task<AsterloomProtocolTokenResult> AuthenticateWithPasswordAsync(
            string registrationId,
            string username,
            string password,
            IReadOnlyCollection<string> scopes,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _passwordCalls);
            LastPasswordUsername = username;
            LastPassword = password;
            LastPasswordScopes = scopes.ToArray();
            if (PasswordHandler is not null)
            {
                return PasswordHandler(cancellationToken);
            }

            return Task.FromResult(
                PasswordResult
                ?? throw new InvalidOperationException("No password result configured."));
        }

        public Task SignOutInteractivelyAsync(
            string registrationId,
            string? identityTokenHint,
            CancellationToken cancellationToken)
        {
            LastIdentityTokenHint = identityTokenHint;
            return SignOutException is null
                ? Task.CompletedTask
                : Task.FromException(SignOutException);
        }

        private int _refreshCalls;
        private int _passwordCalls;
        private int _serviceCalls;
    }
}
