namespace Asterloom.Sdk.Identity;

public sealed class AsterloomIdentityClientOptions
{
    public Uri? Issuer { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public string RegistrationId { get; set; } = "asterloom";

    public bool EnableInteractiveAuthentication { get; set; }

    public bool EnableServiceCredentials { get; set; }

    public bool EnablePasswordAuthentication { get; set; }

    public bool RequestRefreshTokens { get; set; } = true;

    public Uri RedirectUri { get; set; } = new("http://localhost/", UriKind.Absolute);

    public Uri PostLogoutRedirectUri { get; set; } =
        new("http://localhost/", UriKind.Absolute);

    public ICollection<string> Scopes { get; } = new List<string> { "asterloom.api" };

    public ICollection<int> AllowedEmbeddedWebServerPorts { get; } = new List<int>();

    public TimeSpan RefreshBeforeExpiration { get; set; } = TimeSpan.FromMinutes(1);

    public bool AllowInsecureHttpForDevelopment { get; set; }
}

public interface IAsterloomTokenStore
{
    ValueTask<AsterloomTokenSet?> ReadAsync(CancellationToken cancellationToken = default);

    ValueTask WriteAsync(
        AsterloomTokenSet tokens,
        CancellationToken cancellationToken = default);

    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class AsterloomInMemoryTokenStore : IAsterloomTokenStore
{
    private readonly object _gate = new();
    private AsterloomTokenSet? _tokens;

    public ValueTask<AsterloomTokenSet?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_tokens);
        }
    }

    public ValueTask WriteAsync(
        AsterloomTokenSet tokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _tokens = tokens;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _tokens = null;
        }

        return ValueTask.CompletedTask;
    }
}
