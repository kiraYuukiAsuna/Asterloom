namespace Asterloom.Modules.Telemetry.Model;

public readonly record struct TelemetryScope(
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId);

public enum TelemetryResourceStatus
{
    Active = 1,
    Archived = 2,
}

public enum TelemetryOtlpProtocol
{
    Grpc = 1,
    HttpProtobuf = 2,
}

public enum TelemetryCollectorStatus
{
    Healthy = 1,
    Degraded = 2,
    Unavailable = 3,
}

public enum TelemetrySignalType
{
    Trace = 1,
    Metric = 2,
    Log = 3,
}

public sealed record TelemetrySource(
    Guid Id,
    TelemetryScope Scope,
    string Key,
    string DisplayName,
    string Description,
    string ServiceName,
    string ResourceAttributesJson,
    TelemetryResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record TelemetrySettings(
    TelemetryScope Scope,
    double SamplingRatio,
    bool TracesEnabled,
    bool MetricsEnabled,
    bool LogsEnabled,
    string ExporterEndpoint,
    TelemetryOtlpProtocol ExporterProtocol,
    string DiagnosticsBaseUrl,
    long Version,
    DateTimeOffset UpdatedAt);

public sealed record TelemetryCollectorHealth(
    TelemetryCollectorStatus Status,
    string Endpoint,
    DateTimeOffset CheckedAt,
    long LatencyMilliseconds,
    string Message);

public sealed record TelemetryError(
    Guid Id,
    TelemetryScope? Scope,
    string ServiceName,
    string ExceptionType,
    string Message,
    string GrpcMethod,
    string TraceId,
    string SpanId,
    string RequestId,
    DateTimeOffset OccurredAt);

public sealed record TelemetryRecord(
    Guid Id,
    TelemetryScope Scope,
    TelemetrySignalType SignalType,
    string ServiceName,
    DateTimeOffset ObservedAt,
    string TraceId,
    string SpanId,
    string Name,
    string Category,
    string Value,
    double? DurationMilliseconds,
    string AttributesJson,
    string PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record TelemetryDiagnosticLink(
    string Url,
    string TraceId,
    DateTimeOffset FromAt,
    DateTimeOffset ToAt);

public sealed record TelemetryPageRequest(
    int Offset,
    int PageSize,
    string Query,
    bool IncludeArchived);

public sealed record TelemetryErrorFilter(
    int Offset,
    int PageSize,
    string ServiceName,
    string TraceId);

public sealed record TelemetryRecordFilter(
    int Offset,
    int PageSize,
    TelemetrySignalType SignalType,
    string ServiceName,
    string TraceId,
    string Query,
    DateTimeOffset? FromAt,
    DateTimeOffset? ToAt);

public sealed record TelemetryStorePage<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed record TelemetryListResult<T>(
    IReadOnlyList<T> Items,
    string NextPageToken);
