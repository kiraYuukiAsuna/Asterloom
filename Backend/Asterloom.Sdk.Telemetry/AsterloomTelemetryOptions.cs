using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;

namespace Asterloom.Sdk.Telemetry;

public sealed class AsterloomTelemetryOptions
{
    public bool Enabled { get; set; } = true;

    public required string ServiceName { get; set; }

    public string ServiceNamespace { get; set; } = "Asterloom";

    public string ServiceVersion { get; set; } = string.Empty;

    public string ServiceInstanceId { get; set; } = Environment.MachineName;

    public string EnvironmentName { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string ApplicationId { get; set; } = string.Empty;

    public string EnvironmentId { get; set; } = string.Empty;

    public Uri ExporterEndpoint { get; set; } = new("http://localhost:4317");

    public OtlpExportProtocol ExporterProtocol { get; set; } = OtlpExportProtocol.Grpc;

    public double SamplingRatio { get; set; } = 1;

    public bool TracesEnabled { get; set; } = true;

    public bool MetricsEnabled { get; set; } = true;

    public bool LogsEnabled { get; set; } = true;

    public bool AspNetCoreInstrumentationEnabled { get; set; } = true;

    public bool HttpClientInstrumentationEnabled { get; set; } = true;

    public ICollection<string> ActivitySourceNames { get; } = new List<string>();

    public ICollection<string> MeterNames { get; } = new List<string>();

    public IDictionary<string, object> ResourceAttributes { get; } =
        new Dictionary<string, object>(StringComparer.Ordinal);

    public static AsterloomTelemetryOptions FromConfiguration(
        IConfiguration configuration,
        string serviceName,
        string? serviceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var endpointValue = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? configuration["Telemetry:ExporterEndpoint"];
        var options = new AsterloomTelemetryOptions
        {
            ServiceName = serviceName,
            ServiceVersion = serviceVersion ?? string.Empty,
            Enabled = bool.TryParse(configuration["Telemetry:Enabled"], out var enabled)
                ? enabled
                : !string.IsNullOrWhiteSpace(endpointValue),
            EnvironmentName = configuration["OTEL_RESOURCE_ATTRIBUTES:deployment.environment.name"]
                ?? configuration["Telemetry:EnvironmentName"]
                ?? string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(endpointValue))
        {
            options.ExporterEndpoint = new Uri(endpointValue, UriKind.Absolute);
        }

        if (string.Equals(
            configuration["OTEL_EXPORTER_OTLP_PROTOCOL"],
            "http/protobuf",
            StringComparison.OrdinalIgnoreCase))
        {
            options.ExporterProtocol = OtlpExportProtocol.HttpProtobuf;
        }

        if (double.TryParse(
            configuration["Telemetry:SamplingRatio"],
            System.Globalization.CultureInfo.InvariantCulture,
            out var ratio))
        {
            options.SamplingRatio = ratio;
        }

        return options;
    }
}
