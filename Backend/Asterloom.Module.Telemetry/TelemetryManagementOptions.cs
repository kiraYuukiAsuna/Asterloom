using Microsoft.Extensions.Configuration;

namespace Asterloom.Modules.Telemetry;

public sealed record TelemetryManagementOptions(
    Uri CollectorHealthEndpoint,
    string DefaultExporterEndpoint,
    string DefaultDiagnosticsBaseUrl,
    string IngestionApiKey)
{
    public static TelemetryManagementOptions FromConfiguration(IConfiguration configuration)
    {
        var healthValue = configuration["Telemetry:CollectorHealthEndpoint"]
            ?? "http://localhost:13133/";
        if (!Uri.TryCreate(healthValue, UriKind.Absolute, out var healthEndpoint)
            || healthEndpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Telemetry:CollectorHealthEndpoint must be an absolute HTTP(S) URL.");
        }

        return new(
            healthEndpoint,
            configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                ?? configuration["Telemetry:DefaultExporterEndpoint"]
                ?? "http://localhost:4317",
            configuration["Telemetry:DiagnosticsBaseUrl"] ?? string.Empty,
            configuration["Telemetry:IngestionApiKey"] ?? string.Empty);
    }
}
