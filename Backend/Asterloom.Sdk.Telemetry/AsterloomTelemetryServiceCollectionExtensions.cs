using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Asterloom.UnitTests")]

namespace Asterloom.Sdk.Telemetry;

public static class AsterloomTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddAsterloomTelemetry(
        this IServiceCollection services,
        AsterloomTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        Validate(options);
        if (!options.Enabled)
        {
            return services;
        }

        var builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => ConfigureResource(resource, options));

        if (options.TracesEnabled)
        {
            builder.WithTracing(tracing =>
            {
                tracing.SetSampler(new TraceIdRatioBasedSampler(options.SamplingRatio));
                if (options.AspNetCoreInstrumentationEnabled)
                {
                    tracing.AddAspNetCoreInstrumentation(instrumentation =>
                    {
                        instrumentation.RecordException = true;
                    });
                }

                if (options.HttpClientInstrumentationEnabled)
                {
                    tracing.AddHttpClientInstrumentation(instrumentation =>
                    {
                        instrumentation.RecordException = true;
                    });
                }

                if (options.ActivitySourceNames.Count > 0)
                {
                    tracing.AddSource([.. options.ActivitySourceNames]);
                }

                tracing.AddOtlpExporter(exporter => ConfigureExporter(exporter, options, "traces"));
            });
        }

        if (options.MetricsEnabled)
        {
            builder.WithMetrics(metrics =>
            {
                metrics.AddRuntimeInstrumentation();
                if (options.AspNetCoreInstrumentationEnabled)
                {
                    metrics.AddAspNetCoreInstrumentation();
                }

                if (options.HttpClientInstrumentationEnabled)
                {
                    metrics.AddHttpClientInstrumentation();
                }

                if (options.MeterNames.Count > 0)
                {
                    metrics.AddMeter([.. options.MeterNames]);
                }

                metrics.AddOtlpExporter(exporter => ConfigureExporter(exporter, options, "metrics"));
            });
        }

        return services;
    }

    public static ILoggingBuilder AddAsterloomTelemetryLogging(
        this ILoggingBuilder logging,
        AsterloomTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(logging);
        Validate(options);
        if (!options.Enabled || !options.LogsEnabled)
        {
            return logging;
        }

        logging.AddOpenTelemetry(openTelemetry =>
        {
            openTelemetry.IncludeFormattedMessage = true;
            openTelemetry.IncludeScopes = true;
            openTelemetry.ParseStateValues = true;
            openTelemetry.SetResourceBuilder(ConfigureResource(ResourceBuilder.CreateDefault(), options));
            openTelemetry.AddOtlpExporter(exporter => ConfigureExporter(exporter, options, "logs"));
        });
        return logging;
    }

    private static ResourceBuilder ConfigureResource(
        ResourceBuilder resource,
        AsterloomTelemetryOptions options)
    {
        resource.AddService(
            serviceName: options.ServiceName,
            serviceNamespace: options.ServiceNamespace,
            serviceVersion: options.ServiceVersion,
            autoGenerateServiceInstanceId: false,
            serviceInstanceId: options.ServiceInstanceId);
        var attributes = new Dictionary<string, object>(options.ResourceAttributes, StringComparer.Ordinal);
        AddIfNotEmpty(attributes, "deployment.environment.name", options.EnvironmentName);
        AddIfNotEmpty(attributes, "asterloom.tenant.id", options.TenantId);
        AddIfNotEmpty(attributes, "asterloom.application.id", options.ApplicationId);
        AddIfNotEmpty(attributes, "asterloom.environment.id", options.EnvironmentId);
        return attributes.Count == 0 ? resource : resource.AddAttributes(attributes);
    }

    private static void ConfigureExporter(
        OtlpExporterOptions exporter,
        AsterloomTelemetryOptions options,
        string signal)
    {
        exporter.Endpoint = ResolveExporterEndpoint(options, signal);
        exporter.Protocol = options.ExporterProtocol;
    }

    internal static Uri ResolveExporterEndpoint(
        AsterloomTelemetryOptions options,
        string signal)
    {
        if (options.ExporterProtocol != OtlpExportProtocol.HttpProtobuf)
        {
            return options.ExporterEndpoint;
        }

        var endpoint = new UriBuilder(options.ExporterEndpoint)
        {
            Path = $"{options.ExporterEndpoint.AbsolutePath.TrimEnd('/')}/v1/{signal}",
        };
        return endpoint.Uri;
    }

    private static void Validate(AsterloomTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            throw new ArgumentException("A telemetry service name is required.", nameof(options));
        }

        if (!double.IsFinite(options.SamplingRatio) || options.SamplingRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The telemetry sampling ratio must be between 0 and 1.");
        }

        if (options.ExporterEndpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(options.ExporterEndpoint.UserInfo))
        {
            throw new ArgumentException(
                "The OTLP endpoint must be an absolute HTTP(S) URL without credentials.",
                nameof(options));
        }
    }

    private static void AddIfNotEmpty(
        Dictionary<string, object> attributes,
        string key,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[key] = value;
        }
    }
}
