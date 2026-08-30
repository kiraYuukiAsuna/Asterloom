namespace Asterloom.Modules.Auditing;

public enum AuditOutcome
{
    Succeeded = 1,
    Denied = 2,
    Failed = 3,
}

public sealed record AsterloomAuditEvent(
    Guid Id,
    string ActorId,
    Guid? TenantId,
    Guid? ApplicationId,
    Guid? EnvironmentId,
    string Operation,
    string ResourceType,
    string ResourceId,
    string RequestId,
    AuditOutcome Outcome,
    string ErrorCode,
    string ChangeSummary,
    DateTimeOffset CreatedAt);

public sealed record AuditPageRequest(
    int Offset,
    int PageSize,
    string ActorId,
    string Operation,
    AuditOutcome? Outcome,
    string RequestId,
    DateTimeOffset? FromAt,
    DateTimeOffset? ToAt);

public sealed record AuditPage(
    IReadOnlyList<AsterloomAuditEvent> Items,
    bool HasMore);

public interface IAuditStore
{
    Task AppendAsync(AsterloomAuditEvent auditEvent, CancellationToken cancellationToken);

    Task<AuditPage> ListAsync(
        AuditPageRequest request,
        CancellationToken cancellationToken);

    Task<AsterloomAuditEvent?> GetAsync(
        Guid auditEventId,
        CancellationToken cancellationToken);
}
