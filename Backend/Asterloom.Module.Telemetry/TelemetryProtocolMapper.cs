using Asterloom.Modules.Telemetry.Model;
using Google.Protobuf.WellKnownTypes;
using ProtocolCollectorHealth = Asterloom.Protocol.Telemetry.V1.CollectorHealth;
using ProtocolCollectorStatus = Asterloom.Protocol.Telemetry.V1.CollectorHealthStatus;
using ProtocolDiagnosticLink = Asterloom.Protocol.Telemetry.V1.DiagnosticLink;
using ProtocolOtlpProtocol = Asterloom.Protocol.Telemetry.V1.OtlpProtocol;
using ProtocolResourceStatus = Asterloom.Protocol.Telemetry.V1.TelemetryResourceStatus;
using ProtocolScope = Asterloom.Protocol.Telemetry.V1.TelemetryScope;
using ProtocolSettings = Asterloom.Protocol.Telemetry.V1.TelemetrySettings;
using ProtocolSource = Asterloom.Protocol.Telemetry.V1.TelemetrySource;
using ProtocolTelemetryError = Asterloom.Protocol.Telemetry.V1.TelemetryError;

namespace Asterloom.Modules.Telemetry;

internal static class TelemetryProtocolMapper
{
    public static ProtocolScope ToProtocol(this TelemetryScope scope) => new()
    {
        TenantId = scope.TenantId.ToString("D"),
        ApplicationId = scope.ApplicationId.ToString("D"),
        EnvironmentId = scope.EnvironmentId.ToString("D"),
    };

    public static ProtocolSource ToProtocol(this TelemetrySource source) => new()
    {
        Id = source.Id.ToString("D"),
        Scope = source.Scope.ToProtocol(),
        Key = source.Key,
        DisplayName = source.DisplayName,
        Description = source.Description,
        ServiceName = source.ServiceName,
        ResourceAttributesJson = source.ResourceAttributesJson,
        Status = source.Status switch
        {
            TelemetryResourceStatus.Active => ProtocolResourceStatus.Active,
            TelemetryResourceStatus.Archived => ProtocolResourceStatus.Archived,
            _ => ProtocolResourceStatus.Unspecified,
        },
        Version = source.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(source.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(source.UpdatedAt),
        ArchivedAt = source.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolSettings ToProtocol(this TelemetrySettings settings) => new()
    {
        Scope = settings.Scope.ToProtocol(),
        SamplingRatio = settings.SamplingRatio,
        TracesEnabled = settings.TracesEnabled,
        MetricsEnabled = settings.MetricsEnabled,
        LogsEnabled = settings.LogsEnabled,
        ExporterEndpoint = settings.ExporterEndpoint,
        ExporterProtocol = settings.ExporterProtocol.ToProtocol(),
        DiagnosticsBaseUrl = settings.DiagnosticsBaseUrl,
        Version = settings.Version,
        UpdatedAt = Timestamp.FromDateTimeOffset(settings.UpdatedAt),
    };

    public static ProtocolCollectorHealth ToProtocol(this TelemetryCollectorHealth health) => new()
    {
        Status = health.Status switch
        {
            TelemetryCollectorStatus.Healthy => ProtocolCollectorStatus.Healthy,
            TelemetryCollectorStatus.Degraded => ProtocolCollectorStatus.Degraded,
            TelemetryCollectorStatus.Unavailable => ProtocolCollectorStatus.Unavailable,
            _ => ProtocolCollectorStatus.Unspecified,
        },
        Endpoint = health.Endpoint,
        CheckedAt = Timestamp.FromDateTimeOffset(health.CheckedAt),
        LatencyMilliseconds = health.LatencyMilliseconds,
        Message = health.Message,
    };

    public static ProtocolTelemetryError ToProtocol(this TelemetryError error) => new()
    {
        Id = error.Id.ToString("D"),
        Scope = error.Scope is { } scope ? scope.ToProtocol() : new ProtocolScope(),
        ServiceName = error.ServiceName,
        ExceptionType = error.ExceptionType,
        Message = error.Message,
        GrpcMethod = error.GrpcMethod,
        TraceId = error.TraceId,
        SpanId = error.SpanId,
        RequestId = error.RequestId,
        OccurredAt = Timestamp.FromDateTimeOffset(error.OccurredAt),
    };

    public static ProtocolDiagnosticLink ToProtocol(this TelemetryDiagnosticLink link) => new()
    {
        Url = link.Url,
        TraceId = link.TraceId,
        FromAt = Timestamp.FromDateTimeOffset(link.FromAt),
        ToAt = Timestamp.FromDateTimeOffset(link.ToAt),
    };

    public static TelemetryOtlpProtocol ToDomain(this ProtocolOtlpProtocol protocol) =>
        protocol switch
        {
            ProtocolOtlpProtocol.Grpc => TelemetryOtlpProtocol.Grpc,
            ProtocolOtlpProtocol.HttpProtobuf => TelemetryOtlpProtocol.HttpProtobuf,
            _ => (TelemetryOtlpProtocol)0,
        };

    private static ProtocolOtlpProtocol ToProtocol(this TelemetryOtlpProtocol protocol) =>
        protocol switch
        {
            TelemetryOtlpProtocol.Grpc => ProtocolOtlpProtocol.Grpc,
            TelemetryOtlpProtocol.HttpProtobuf => ProtocolOtlpProtocol.HttpProtobuf,
            _ => ProtocolOtlpProtocol.Unspecified,
        };
}
