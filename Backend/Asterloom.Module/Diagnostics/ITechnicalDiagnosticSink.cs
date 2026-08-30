namespace Asterloom.Modules.Diagnostics;

public sealed record TechnicalDiagnostic(
    Guid Id,
    Guid? TenantId,
    Guid? ApplicationId,
    Guid? EnvironmentId,
    string ServiceName,
    string ExceptionType,
    string Message,
    string GrpcMethod,
    string TraceId,
    string SpanId,
    string RequestId,
    DateTimeOffset OccurredAt);

public interface ITechnicalDiagnosticSink
{
    ValueTask RecordAsync(
        TechnicalDiagnostic diagnostic,
        CancellationToken cancellationToken);
}
