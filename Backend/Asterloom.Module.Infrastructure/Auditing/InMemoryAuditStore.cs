using Asterloom.Modules.Auditing;

namespace Asterloom.Modules.Infrastructure.Auditing;

internal sealed class InMemoryAuditStore : IAuditStore
{
    private readonly Lock _gate = new();
    private readonly List<AsterloomAuditEvent> _events = [];

    public Task AppendAsync(
        AsterloomAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_events.Any(candidate => candidate.Id == auditEvent.Id))
            {
                throw new InvalidOperationException(
                    $"Audit event '{auditEvent.Id}' already exists.");
            }

            _events.Add(auditEvent);
        }

        return Task.CompletedTask;
    }

    public Task<AuditPage> ListAsync(
        AuditPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var items = _events
                .Where(auditEvent => string.IsNullOrEmpty(request.ActorId)
                    || auditEvent.ActorId.Contains(
                        request.ActorId,
                        StringComparison.OrdinalIgnoreCase))
                .Where(auditEvent => string.IsNullOrEmpty(request.Operation)
                    || auditEvent.Operation.Contains(
                        request.Operation,
                        StringComparison.OrdinalIgnoreCase))
                .Where(auditEvent => request.Outcome is null
                    || auditEvent.Outcome == request.Outcome)
                .Where(auditEvent => string.IsNullOrEmpty(request.RequestId)
                    || string.Equals(
                        auditEvent.RequestId,
                        request.RequestId,
                        StringComparison.OrdinalIgnoreCase))
                .Where(auditEvent => request.FromAt is null
                    || auditEvent.CreatedAt >= request.FromAt)
                .Where(auditEvent => request.ToAt is null
                    || auditEvent.CreatedAt <= request.ToAt)
                .OrderByDescending(static auditEvent => auditEvent.CreatedAt)
                .ThenByDescending(static auditEvent => auditEvent.Id)
                .Skip(request.Offset)
                .Take(request.PageSize + 1)
                .ToArray();
            return Task.FromResult(new AuditPage(
                items.Take(request.PageSize).ToArray(),
                items.Length > request.PageSize));
        }
    }

    public Task<AsterloomAuditEvent?> GetAsync(
        Guid auditEventId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _events.FirstOrDefault(auditEvent => auditEvent.Id == auditEventId));
        }
    }
}
