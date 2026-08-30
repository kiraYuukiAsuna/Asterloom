namespace Asterloom.ReferenceApp.Client;

internal sealed record ReferenceAppSettings(
    Uri AsterloomBaseAddress,
    Uri PassportIssuer,
    Uri ReferenceBackendAddress,
    Uri ReferenceBackendGrpcAddress,
    string ServiceClientId,
    string ServiceClientSecret,
    string InteractiveClientId,
    string StateFile,
    bool AllowInsecureDevelopment)
{
    public static ReferenceAppSettings Load()
    {
        var baseAddress = ReadUri(
            "ASTERLOOM_BASE_URL",
            "https://asterloom.kirayuukiasuna.cloud/");
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
            ReadRequired("ASTERLOOM_REFERENCE_CLIENT_ID"),
            ReadRequired("ASTERLOOM_REFERENCE_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable("ASTERLOOM_REFERENCE_INTERACTIVE_CLIENT_ID")
                ?.Trim() ?? "asterloom-reference-native",
            Path.GetFullPath(stateFile),
            ReadBoolean("ASTERLOOM_ALLOW_INSECURE_DEVELOPMENT")
                || (baseAddress.IsLoopback && baseAddress.Scheme == Uri.UriSchemeHttp));
    }

    private static string ReadRequired(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        return !string.IsNullOrEmpty(value)
            ? value
            : throw new InvalidOperationException($"Environment variable {name} is required.");
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
