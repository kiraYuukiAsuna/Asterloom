using Google.Protobuf.WellKnownTypes;
using ProtocolAuditEvent = Asterloom.Protocol.Audit.V1.AuditEvent;
using ProtocolAuditOutcome = Asterloom.Protocol.Audit.V1.AuditOutcome;

namespace Asterloom.Modules.Auditing;

internal static class AuditProtocolMapper
{
    public static ProtocolAuditEvent ToProtocol(this AsterloomAuditEvent auditEvent) => new()
    {
        Id = auditEvent.Id.ToString("D"),
        ActorId = auditEvent.ActorId,
        TenantId = auditEvent.TenantId?.ToString("D") ?? string.Empty,
        ApplicationId = auditEvent.ApplicationId?.ToString("D") ?? string.Empty,
        EnvironmentId = auditEvent.EnvironmentId?.ToString("D") ?? string.Empty,
        Operation = auditEvent.Operation,
        ResourceType = auditEvent.ResourceType,
        ResourceId = auditEvent.ResourceId,
        RequestId = auditEvent.RequestId,
        Outcome = auditEvent.Outcome.ToProtocol(),
        ErrorCode = auditEvent.ErrorCode,
        ChangeSummary = auditEvent.ChangeSummary,
        CreatedAt = Timestamp.FromDateTimeOffset(auditEvent.CreatedAt),
    };

    public static AuditOutcome? ToDomain(this ProtocolAuditOutcome outcome) => outcome switch
    {
        ProtocolAuditOutcome.Unspecified => null,
        ProtocolAuditOutcome.Succeeded => AuditOutcome.Succeeded,
        ProtocolAuditOutcome.Denied => AuditOutcome.Denied,
        ProtocolAuditOutcome.Failed => AuditOutcome.Failed,
        _ => (AuditOutcome)0,
    };

    private static ProtocolAuditOutcome ToProtocol(this AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Succeeded => ProtocolAuditOutcome.Succeeded,
        AuditOutcome.Denied => ProtocolAuditOutcome.Denied,
        AuditOutcome.Failed => ProtocolAuditOutcome.Failed,
        _ => ProtocolAuditOutcome.Unspecified,
    };
}
