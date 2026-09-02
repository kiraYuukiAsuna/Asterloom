namespace Asterloom.ReferenceApp.Client;

internal sealed record ReferenceAppSettings(
    Uri AsterloomBaseAddress,
    Uri PassportIssuer,
    Uri ReferenceBackendAddress,
    Uri ReferenceBackendGrpcAddress,
    string ServiceClientId,
    string ServiceClientSecret,
    string InteractiveClientId,
    string InteractiveApiScope,
    string StateFile,
    ReferenceDesktopReleaseSettings DesktopRelease,
    bool AllowInsecureDevelopment)
{
    public (string ClientId, string ClientSecret) RequireServiceCredentials()
    {
        if (string.IsNullOrWhiteSpace(ServiceClientId)
            || string.IsNullOrWhiteSpace(ServiceClientSecret))
        {
            throw new InvalidOperationException(
                "ASTERLOOM_REFERENCE_CLIENT_ID and ASTERLOOM_REFERENCE_CLIENT_SECRET are required for service commands only.");
        }

        return (ServiceClientId, ServiceClientSecret);
    }

    public static ReferenceAppSettings Load()
    {
        var baseAddress = ReadUri(
            "ASTERLOOM_BASE_URL",
            "https://asterloom.momiya.cloud/");
        var issuer = ReadUri("ASTERLOOM_ISSUER", baseAddress.AbsoluteUri);
        var referenceBackend = ReadUri(
            "ASTERLOOM_REFERENCE_BACKEND_URL",
            "http://localhost:5090/");
        var referenceBackendGrpc = ReadUri(
            "ASTERLOOM_REFERENCE_BACKEND_GRPC_URL",
            "http://localhost:5091/");
        var stateFile = Environment.GetEnvironmentVariable("ASTERLOOM_REFERENCE_STATE_FILE");
        if (string.IsNullOrWhiteSpace(stateFile))
        {
            stateFile = Path.Combine(AppContext.BaseDirectory, "reference-state.json");
        }

        return new ReferenceAppSettings(
            baseAddress,
            issuer,
            referenceBackend,
            referenceBackendGrpc,
            Environment.GetEnvironmentVariable("ASTERLOOM_REFERENCE_CLIENT_ID")?.Trim()
                ?? string.Empty,
            Environment.GetEnvironmentVariable("ASTERLOOM_REFERENCE_CLIENT_SECRET")
                ?? string.Empty,
            Environment.GetEnvironmentVariable("ASTERLOOM_REFERENCE_INTERACTIVE_CLIENT_ID")
                ?.Trim() ?? "asterloom-reference-native",
            Environment.GetEnvironmentVariable("ASTERLOOM_REFERENCE_API_SCOPE")
                ?.Trim() ?? "asterloom.reference.api",
            Path.GetFullPath(stateFile),
            ReferenceDesktopReleaseSettings.Load(),
            ReadBoolean("ASTERLOOM_ALLOW_INSECURE_DEVELOPMENT")
                || (baseAddress.IsLoopback && baseAddress.Scheme == Uri.UriSchemeHttp));
    }

    private static Uri ReadUri(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return Uri.TryCreate(
            string.IsNullOrWhiteSpace(value) ? fallback : value,
            UriKind.Absolute,
            out var uri)
            ? uri
            : throw new InvalidOperationException($"Environment variable {name} is not a valid URI.");
    }

    private static bool ReadBoolean(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value;
}
