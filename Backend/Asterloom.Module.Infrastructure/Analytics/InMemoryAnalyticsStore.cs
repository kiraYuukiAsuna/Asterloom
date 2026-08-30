using Asterloom.Modules.Analytics.Model;
using Asterloom.Modules.Analytics.Persistence;

namespace Asterloom.Modules.Infrastructure.Analytics;

internal sealed class InMemoryAnalyticsStore : IAnalyticsStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, EventSchema> _schemas = [];
    private readonly Dictionary<Guid, AnalyticsWriteKey> _writeKeys = [];
    private readonly Dictionary<Guid, StoredAnalyticsEvent> _events = [];
    private readonly Dictionary<(AnalyticsScope Scope, string EventId), Guid> _deduplication = [];

    public Task<AnalyticsStorePage<EventSchema>> ListEventSchemasAsync(
        AnalyticsScope scope,
        AnalyticsPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ToPage(
                _schemas.Values
                    .Where(item => item.Scope == scope)
                    .Where(item => request.IncludeInactive
                        || item.Status == AnalyticsResourceStatus.Active)
                    .Where(item => Matches(request.Query, item.Key, item.DisplayName, item.Description))
                    .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.Id),
                request.Offset,
                request.PageSize));
        }
    }

    public Task<EventSchema?> GetEventSchemaAsync(
        AnalyticsScope scope,
        Guid eventSchemaId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _schemas.TryGetValue(eventSchemaId, out var schema) && schema.Scope == scope
                    ? schema
                    : null);
        }
    }

    public Task<EventSchema?> GetActiveEventSchemaByKeyAsync(
        AnalyticsScope scope,
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_schemas.Values.FirstOrDefault(item =>
                item.Scope == scope
                && item.Status == AnalyticsResourceStatus.Active
                && string.Equals(item.Key, key, StringComparison.Ordinal)));
        }
    }

    public Task<bool> TryCreateEventSchemaAsync(
        EventSchema schema,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_schemas.ContainsKey(schema.Id)
                || _schemas.Values.Any(item => item.Scope == schema.Scope
                    && string.Equals(item.Key, schema.Key, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            _schemas.Add(schema.Id, schema);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateEventSchemaAsync(
        EventSchema schema,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_schemas.TryGetValue(schema.Id, out var current)
                || current.Scope != schema.Scope
                || current.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }

            _schemas[schema.Id] = schema;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<AnalyticsWriteKey>> ListWriteKeysAsync(
        AnalyticsScope scope,
        bool includeRevoked,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<AnalyticsWriteKey> result = _writeKeys.Values
                .Where(item => item.Scope == scope)
                .Where(item => includeRevoked || item.Status == AnalyticsWriteKeyStatus.Active)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Id)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<AnalyticsWriteKey?> GetWriteKeyAsync(
        AnalyticsScope scope,
        Guid writeKeyId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _writeKeys.TryGetValue(writeKeyId, out var writeKey) && writeKey.Scope == scope
                    ? writeKey
                    : null);
        }
    }

    public Task<AnalyticsWriteKey?> GetWriteKeyByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_writeKeys.Values.FirstOrDefault(item =>
                string.Equals(item.Prefix, prefix, StringComparison.Ordinal)));
        }
    }

    public Task<bool> TryCreateWriteKeyAsync(
        AnalyticsWriteKey writeKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_writeKeys.ContainsKey(writeKey.Id)
                || _writeKeys.Values.Any(item => string.Equals(
                    item.Prefix,
                    writeKey.Prefix,
                    StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            _writeKeys.Add(writeKey.Id, writeKey);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateWriteKeyAsync(
        AnalyticsWriteKey writeKey,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_writeKeys.TryGetValue(writeKey.Id, out var current)
                || current.Scope != writeKey.Scope
                || current.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }

            _writeKeys[writeKey.Id] = writeKey;
            return Task.FromResult(true);
        }
    }

    public Task TouchWriteKeyAsync(
        Guid writeKeyId,
        DateTimeOffset lastUsedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_writeKeys.TryGetValue(writeKeyId, out var writeKey)
                && writeKey.Status == AnalyticsWriteKeyStatus.Active)
            {
                _writeKeys[writeKeyId] = writeKey with
                {
                    LastUsedAt = lastUsedAt,
                    UpdatedAt = lastUsedAt,
                };
            }

            return Task.CompletedTask;
        }
    }

    public Task<AnalyticsAppendOutcome> AppendEventAsync(
        StoredAnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var deduplicationKey = (analyticsEvent.Scope, analyticsEvent.EventId);
            if (_deduplication.ContainsKey(deduplicationKey))
            {
                return Task.FromResult(AnalyticsAppendOutcome.Deduplicated);
            }

            _events.Add(analyticsEvent.Id, analyticsEvent);
            _deduplication.Add(deduplicationKey, analyticsEvent.Id);
            return Task.FromResult(AnalyticsAppendOutcome.Accepted);
        }
    }

    public Task<AnalyticsStorePage<StoredAnalyticsEvent>> ListEventsAsync(
        AnalyticsScope scope,
        AnalyticsEventFilter filter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ToPage(
                FilterEvents(scope, filter)
                    .OrderByDescending(static item => item.ReceivedAt)
                    .ThenByDescending(static item => item.Id),
                filter.Offset,
                filter.PageSize));
        }
    }

    public Task<StoredAnalyticsEvent?> GetEventAsync(
        AnalyticsScope scope,
        Guid analyticsEventId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _events.TryGetValue(analyticsEventId, out var analyticsEvent)
                    && analyticsEvent.Scope == scope
                        ? analyticsEvent
                        : null);
        }
    }

    public Task<IReadOnlyList<AnalyticsAggregationBucket>> AggregateAsync(
        AnalyticsAggregationQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var names = query.EventNames.ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<AnalyticsAggregationBucket> result = _events.Values
                .Where(item => item.Scope == query.Scope)
                .Where(item => item.OccurredAt >= query.FromAt && item.OccurredAt <= query.ToAt)
                .Where(item => names.Count == 0 || names.Contains(item.EventName))
                .GroupBy(item => new
                {
                    Period = Truncate(item.OccurredAt, query.Interval),
                    item.EventName,
                })
                .Select(group => new AnalyticsAggregationBucket(
                    group.Key.Period,
                    group.Key.EventName,
                    group.LongCount(),
                    group.Select(static item => item.ActorId.Length > 0
                            ? item.ActorId
                            : item.AnonymousId)
                        .Distinct(StringComparer.Ordinal)
                        .LongCount()))
                .OrderBy(static item => item.PeriodStart)
                .ThenBy(static item => item.EventName, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<int> PurgeExpiredEventsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var expired = _events.Values
                .Where(item => _schemas.TryGetValue(item.EventSchemaId, out var schema)
                    && item.ReceivedAt < now.AddDays(-schema.RetentionDays))
                .Select(static item => item.Id)
                .ToArray();
            foreach (var id in expired)
            {
                var analyticsEvent = _events[id];
                _events.Remove(id);
                _deduplication.Remove((analyticsEvent.Scope, analyticsEvent.EventId));
            }

            return Task.FromResult(expired.Length);
        }
    }

    private IEnumerable<StoredAnalyticsEvent> FilterEvents(
        AnalyticsScope scope,
        AnalyticsEventFilter filter) => _events.Values
        .Where(item => item.Scope == scope)
        .Where(item => filter.EventName.Length == 0
            || string.Equals(item.EventName, filter.EventName, StringComparison.Ordinal))
        .Where(item => filter.ActorId.Length == 0
            || string.Equals(item.ActorId, filter.ActorId, StringComparison.Ordinal)
            || string.Equals(item.AnonymousId, filter.ActorId, StringComparison.Ordinal))
        .Where(item => filter.EventId.Length == 0
            || string.Equals(item.EventId, filter.EventId, StringComparison.Ordinal))
        .Where(item => filter.FromAt is null || item.OccurredAt >= filter.FromAt)
        .Where(item => filter.ToAt is null || item.OccurredAt <= filter.ToAt);

    private static DateTimeOffset Truncate(DateTimeOffset value, AnalyticsInterval interval)
    {
        var utc = value.UtcDateTime;
        return interval switch
        {
            AnalyticsInterval.Hour => new DateTimeOffset(
                utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero),
            AnalyticsInterval.Day => new DateTimeOffset(
                utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero),
            AnalyticsInterval.Week => StartOfWeek(utc),
            _ => throw new ArgumentOutOfRangeException(nameof(interval)),
        };
    }

    private static DateTimeOffset StartOfWeek(DateTime utc)
    {
        var daysFromMonday = ((int)utc.DayOfWeek + 6) % 7;
        var monday = utc.Date.AddDays(-daysFromMonday);
        return new DateTimeOffset(monday, TimeSpan.Zero);
    }

    private static AnalyticsStorePage<T> ToPage<T>(
        IEnumerable<T> source,
        int offset,
        int pageSize)
    {
        var items = source.Skip(offset).Take(pageSize + 1).ToList();
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new(items, hasMore);
    }

    private static bool Matches(string query, params string[] values) =>
        query.Length == 0
        || values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
}
