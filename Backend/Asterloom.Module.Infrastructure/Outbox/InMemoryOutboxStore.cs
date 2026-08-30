using System.Data.Common;
using Asterloom.Modules.Outbox;

namespace Asterloom.Modules.Infrastructure.Outbox;

internal sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, OutboxMessage> _messages = [];
    private readonly HashSet<(Guid EventId, string ConsumerName)> _receipts = [];

    public Task EnqueueAsync(
        OutboxMessageDraft message,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateDraft(message);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_messages.TryAdd(message.Id, ToMessage(message)))
            {
                throw new InvalidOperationException(
                    $"Outbox event '{message.Id}' already exists.");
            }
        }

        return Task.CompletedTask;
    }

    public Task EnqueueAsync(
        OutboxMessageDraft message,
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "The in-memory outbox does not participate in database transactions.");

    public Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        string workerId,
        IReadOnlyCollection<string> eventTypes,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateWorkerId(workerId);
        var types = OutboxValidation.ValidateEventTypes(eventTypes).ToHashSet(StringComparer.Ordinal);
        OutboxValidation.ValidateClaim(leaseDuration, maximumCount);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var candidates = _messages.Values
                .Where(message => types.Contains(message.EventType))
                .Where(message => message.ProcessedAt is null && message.DeadLetteredAt is null)
                .Where(message => message.AvailableAt <= now)
                .Where(message => message.LockedUntil is null || message.LockedUntil <= now)
                .OrderBy(static message => message.AvailableAt)
                .ThenBy(static message => message.OccurredAt)
                .ThenBy(static message => message.Id)
                .Take(maximumCount)
                .ToArray();
            var claimed = new List<OutboxMessage>(candidates.Length);
            foreach (var candidate in candidates)
            {
                var updated = candidate with
                {
                    AttemptCount = candidate.AttemptCount + 1,
                    LockedBy = workerId,
                    LockedUntil = now.Add(leaseDuration),
                };
                _messages[candidate.Id] = updated;
                claimed.Add(updated);
            }

            return Task.FromResult<IReadOnlyList<OutboxMessage>>(claimed);
        }
    }

    public Task<bool> HasReceiptAsync(
        Guid eventId,
        string consumerName,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateConsumerName(consumerName);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_receipts.Contains((eventId, consumerName)));
        }
    }

    public Task<bool> RecordReceiptAsync(
        Guid eventId,
        string consumerName,
        string workerId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateConsumerName(consumerName);
        OutboxValidation.ValidateWorkerId(workerId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsOwned(eventId, workerId))
            {
                return Task.FromResult(false);
            }

            _receipts.Add((eventId, consumerName));
            return Task.FromResult(true);
        }
    }

    public Task<bool> MarkProcessedAsync(
        Guid eventId,
        string workerId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateWorkerId(workerId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsOwned(eventId, workerId))
            {
                return Task.FromResult(false);
            }

            _messages[eventId] = _messages[eventId] with
            {
                LockedBy = null,
                LockedUntil = null,
                ProcessedAt = processedAt,
                LastError = string.Empty,
            };
            return Task.FromResult(true);
        }
    }

    public Task<bool> MarkFailedAsync(
        Guid eventId,
        string workerId,
        DateTimeOffset availableAt,
        string errorCode,
        DateTimeOffset? deadLetteredAt,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateWorkerId(workerId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsOwned(eventId, workerId))
            {
                return Task.FromResult(false);
            }

            _messages[eventId] = _messages[eventId] with
            {
                AvailableAt = availableAt,
                LockedBy = null,
                LockedUntil = null,
                DeadLetteredAt = deadLetteredAt,
                LastError = NormalizeError(errorCode),
            };
            return Task.FromResult(true);
        }
    }

    public Task<OutboxMessage?> GetAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_messages.GetValueOrDefault(eventId));
        }
    }

    private bool IsOwned(Guid eventId, string workerId) =>
        _messages.TryGetValue(eventId, out var message)
        && string.Equals(message.LockedBy, workerId, StringComparison.Ordinal)
        && message.ProcessedAt is null
        && message.DeadLetteredAt is null;

    private static OutboxMessage ToMessage(OutboxMessageDraft message) => new(
        message.Id,
        message.EventType,
        message.SchemaVersion,
        message.Payload,
        message.CorrelationId,
        message.TenantId,
        message.ApplicationId,
        message.EnvironmentId,
        message.OccurredAt,
        message.AvailableAt,
        AttemptCount: 0,
        LockedBy: null,
        LockedUntil: null,
        ProcessedAt: null,
        DeadLetteredAt: null,
        LastError: string.Empty);

    private static string NormalizeError(string value) =>
        string.IsNullOrWhiteSpace(value) ? "handler_failure" : value[..Math.Min(value.Length, 200)];
}
