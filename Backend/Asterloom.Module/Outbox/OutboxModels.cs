using System.Data.Common;
using System.Text.Json;

namespace Asterloom.Modules.Outbox;

public sealed record OutboxMessageDraft(
    Guid Id,
    string EventType,
    int SchemaVersion,
    string Payload,
    string CorrelationId,
    Guid? TenantId,
    Guid? ApplicationId,
    Guid? EnvironmentId,
    DateTimeOffset OccurredAt,
    DateTimeOffset AvailableAt);

public sealed record OutboxMessage(
    Guid Id,
    string EventType,
    int SchemaVersion,
    string Payload,
    string CorrelationId,
    Guid? TenantId,
    Guid? ApplicationId,
    Guid? EnvironmentId,
    DateTimeOffset OccurredAt,
    DateTimeOffset AvailableAt,
    int AttemptCount,
    string? LockedBy,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? DeadLetteredAt,
    string LastError);

public interface IOutboxStore
{
    Task EnqueueAsync(OutboxMessageDraft message, CancellationToken cancellationToken);

    Task EnqueueAsync(
        OutboxMessageDraft message,
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        string workerId,
        IReadOnlyCollection<string> eventTypes,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maximumCount,
        CancellationToken cancellationToken);

    Task<bool> HasReceiptAsync(
        Guid eventId,
        string consumerName,
        CancellationToken cancellationToken);

    Task<bool> RecordReceiptAsync(
        Guid eventId,
        string consumerName,
        string workerId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken);

    Task<bool> MarkProcessedAsync(
        Guid eventId,
        string workerId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken);

    Task<bool> MarkFailedAsync(
        Guid eventId,
        string workerId,
        DateTimeOffset availableAt,
        string errorCode,
        DateTimeOffset? deadLetteredAt,
        CancellationToken cancellationToken);

    Task<OutboxMessage?> GetAsync(Guid eventId, CancellationToken cancellationToken);
}

/// <summary>
/// Handles one integration-event contract. Implementations must be idempotent by event ID.
/// </summary>
public interface IOutboxMessageConsumer
{
    string EventType { get; }

    string ConsumerName { get; }

    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public static class OutboxMessageFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static OutboxMessageDraft Create<TPayload>(
        string eventType,
        int schemaVersion,
        TPayload payload,
        string correlationId,
        DateTimeOffset occurredAt,
        Guid? tenantId = null,
        Guid? applicationId = null,
        Guid? environmentId = null,
        DateTimeOffset? availableAt = null) =>
        new(
            Guid.CreateVersion7(),
            eventType,
            schemaVersion,
            JsonSerializer.Serialize(payload, SerializerOptions),
            correlationId,
            tenantId,
            applicationId,
            environmentId,
            occurredAt,
            availableAt ?? occurredAt);
}
