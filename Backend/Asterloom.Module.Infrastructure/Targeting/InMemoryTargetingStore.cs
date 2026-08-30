using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;

namespace Asterloom.Modules.Infrastructure.Targeting;

internal sealed class InMemoryTargetingStore : ITargetingStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, TargetingSegment> _segments = [];

    public Task<TargetingStorePage<TargetingSegment>> ListSegmentsAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        TargetingPageRequest page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var items = _segments.Values
                .Where(segment =>
                    segment.TenantId == tenantId
                    && segment.ApplicationId == applicationId
                    && segment.EnvironmentId == environmentId
                    && (page.IncludeArchived
                        || segment.Status == TargetingResourceStatus.Active)
                    && Matches(segment, page.Query))
                .OrderBy(static segment => segment.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static segment => segment.Id)
                .Skip(page.Offset)
                .Take(page.PageSize + 1)
                .ToList();
            var hasMore = items.Count > page.PageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            return Task.FromResult(new TargetingStorePage<TargetingSegment>(items, hasMore));
        }
    }

    public Task<TargetingSegment?> GetSegmentAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        Guid segmentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var segment = _segments.GetValueOrDefault(segmentId);
            return Task.FromResult(
                segment is not null
                && segment.TenantId == tenantId
                && segment.ApplicationId == applicationId
                && segment.EnvironmentId == environmentId
                    ? segment
                    : null);
        }
    }

    public Task<bool> TryCreateSegmentAsync(
        TargetingSegment segment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_segments.ContainsKey(segment.Id)
                || _segments.Values.Any(existing =>
                    existing.TenantId == segment.TenantId
                    && existing.ApplicationId == segment.ApplicationId
                    && existing.EnvironmentId == segment.EnvironmentId
                    && string.Equals(existing.Key, segment.Key, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            _segments.Add(segment.Id, segment);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateSegmentAsync(
        TargetingSegment segment,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_segments.TryGetValue(segment.Id, out var current)
                || current.Version != expectedVersion
                || current.TenantId != segment.TenantId
                || current.ApplicationId != segment.ApplicationId
                || current.EnvironmentId != segment.EnvironmentId
                || !string.Equals(current.Key, segment.Key, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _segments[segment.Id] = segment;
            return Task.FromResult(true);
        }
    }

    private static bool Matches(TargetingSegment segment, string query) =>
        string.IsNullOrEmpty(query)
        || segment.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
        || segment.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || segment.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
}
