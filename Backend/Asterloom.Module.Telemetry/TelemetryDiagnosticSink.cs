using Asterloom.Modules.Diagnostics;
using Asterloom.Modules.Telemetry.Model;
using Asterloom.Modules.Telemetry.Persistence;

namespace Asterloom.Modules.Telemetry;

internal sealed class TelemetryDiagnosticSink(ITelemetryStore store)
    : ITechnicalDiagnosticSink
{
    public ValueTask RecordAsync(
        TechnicalDiagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        TelemetryScope? scope = diagnostic is
        {
            TenantId: { } tenantId,
            ApplicationId: { } applicationId,
            EnvironmentId: { } environmentId,
        }
            ? new TelemetryScope(tenantId, applicationId, environmentId)
            : null;
        return new(store.AppendErrorAsync(
            new TelemetryError(
                diagnostic.Id,
                scope,
                diagnostic.ServiceName,
                diagnostic.ExceptionType,
                diagnostic.Message,
                diagnostic.GrpcMethod,
                diagnostic.TraceId,
                diagnostic.SpanId,
                diagnostic.RequestId,
                diagnostic.OccurredAt),
            cancellationToken));
    }
}
