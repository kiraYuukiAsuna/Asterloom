using Asterloom.Modules.Analytics.Model;

namespace Asterloom.Modules.Analytics.Persistence;

public interface IAnalyticsStore
{
    Task<AnalyticsStorePage<EventSchema>> ListEventSchemasAsync(
        AnalyticsScope scope,
        AnalyticsPageRequest request,
        CancellationToken cancellationToken);

    Task<EventSchema?> GetEventSchemaAsync(
        AnalyticsScope scope,
        Guid eventSchemaId,
        CancellationToken cancellationToken);

    Task<EventSchema?> GetActiveEventSchemaByKeyAsync(
        AnalyticsScope scope,
        string key,
        CancellationToken cancellationToken);

    Task<bool> TryCreateEventSchemaAsync(EventSchema schema, CancellationToken cancellationToken);

    Task<bool> TryUpdateEventSchemaAsync(
        EventSchema schema,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalyticsWriteKey>> ListWriteKeysAsync(
        AnalyticsScope scope,
        bool includeRevoked,
        CancellationToken cancellationToken);

    Task<AnalyticsWriteKey?> GetWriteKeyAsync(
        AnalyticsScope scope,
        Guid writeKeyId,
        CancellationToken cancellationToken);

    Task<AnalyticsWriteKey?> GetWriteKeyByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken);

    Task<bool> TryCreateWriteKeyAsync(
        AnalyticsWriteKey writeKey,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateWriteKeyAsync(
        AnalyticsWriteKey writeKey,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task TouchWriteKeyAsync(
        Guid writeKeyId,
        DateTimeOffset lastUsedAt,
        CancellationToken cancellationToken);

    Task<AnalyticsAppendOutcome> AppendEventAsync(
        StoredAnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken);

    Task<AnalyticsStorePage<StoredAnalyticsEvent>> ListEventsAsync(
        AnalyticsScope scope,
        AnalyticsEventFilter filter,
        CancellationToken cancellationToken);

    Task<StoredAnalyticsEvent?> GetEventAsync(
        AnalyticsScope scope,
        Guid analyticsEventId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalyticsAggregationBucket>> AggregateAsync(
        AnalyticsAggregationQuery query,
        CancellationToken cancellationToken);

    Task<int> PurgeExpiredEventsAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
