using Asterloom.Modules.Feature.Model;
using Asterloom.Modules.Feature.Persistence;
using Asterloom.Modules.Outbox;

namespace Asterloom.Modules.Infrastructure.Feature;

internal sealed class InMemoryFeatureStore(IOutboxStore outboxStore) : IFeatureStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, FeatureFlag> _flags = [];
    private readonly Dictionary<(Guid FlagId, long Revision), FeatureRevision> _revisions = [];

    public Task<FeatureStorePage<FeatureFlag>> ListFlagsAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        FeaturePageRequest page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var items = _flags.Values
                .Where(flag =>
                    flag.TenantId == tenantId
                    && flag.ApplicationId == applicationId
                    && flag.EnvironmentId == environmentId
                    && (page.IncludeArchived || flag.Status == FeatureResourceStatus.Active)
                    && Matches(flag, page.Query))
                .OrderBy(static flag => flag.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static flag => flag.Id)
                .Skip(page.Offset)
                .Take(page.PageSize + 1)
                .ToList();
            var hasMore = items.Count > page.PageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            return Task.FromResult(new FeatureStorePage<FeatureFlag>(items, hasMore));
        }
    }

    public Task<FeatureFlag?> GetFlagAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        Guid flagId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _flags.TryGetValue(flagId, out var flag)
                && HasScope(flag, tenantId, applicationId, environmentId)
                    ? flag
                    : null);
        }
    }

    public Task<FeatureFlag?> GetFlagByKeyAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_flags.Values.FirstOrDefault(flag =>
                HasScope(flag, tenantId, applicationId, environmentId)
                && string.Equals(flag.Key, key, StringComparison.Ordinal)));
        }
    }

    public Task<bool> TryCreateFlagAsync(
        FeatureFlag flag,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_flags.ContainsKey(flag.Id)
                || _flags.Values.Any(existing =>
                    HasScope(
                        existing,
                        flag.TenantId,
                        flag.ApplicationId,
                        flag.EnvironmentId)
                    && string.Equals(existing.Key, flag.Key, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            _flags.Add(flag.Id, flag);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateFlagAsync(
        FeatureFlag flag,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!CanUpdate(flag, expectedVersion))
            {
                return Task.FromResult(false);
            }

            _flags[flag.Id] = flag;
            return Task.FromResult(true);
        }
    }

    public Task<FeatureStorePage<FeatureRevision>> ListRevisionsAsync(
        Guid flagId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var items = _revisions.Values
                .Where(revision => revision.FlagId == flagId)
                .OrderByDescending(static revision => revision.Revision)
                .Skip(offset)
                .Take(pageSize + 1)
                .ToList();
            var hasMore = items.Count > pageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            return Task.FromResult(new FeatureStorePage<FeatureRevision>(items, hasMore));
        }
    }

    public Task<FeatureRevision?> GetRevisionAsync(
        Guid flagId,
        long revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_revisions.GetValueOrDefault((flagId, revision)));
        }
    }

    public async Task<bool> TryPublishAsync(
        FeatureFlag flag,
        long expectedVersion,
        FeatureRevision revision,
        OutboxMessageDraft integrationEvent,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!CanUpdate(flag, expectedVersion)
                || _revisions.ContainsKey((revision.FlagId, revision.Revision)))
            {
                return false;
            }

            _flags[flag.Id] = flag;
            _revisions.Add((revision.FlagId, revision.Revision), revision);
        }

        await outboxStore.EnqueueAsync(integrationEvent, cancellationToken);
        return true;
    }

    private bool CanUpdate(FeatureFlag flag, long expectedVersion) =>
        _flags.TryGetValue(flag.Id, out var current)
        && current.Version == expectedVersion
        && HasScope(
            current,
            flag.TenantId,
            flag.ApplicationId,
            flag.EnvironmentId)
        && string.Equals(current.Key, flag.Key, StringComparison.Ordinal);

    private static bool HasScope(
        FeatureFlag flag,
        Guid tenantId,
        Guid applicationId,
        Guid environmentId) =>
        flag.TenantId == tenantId
        && flag.ApplicationId == applicationId
        && flag.EnvironmentId == environmentId;

    private static bool Matches(FeatureFlag flag, string query) =>
        string.IsNullOrEmpty(query)
        || flag.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
        || flag.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || flag.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
}
