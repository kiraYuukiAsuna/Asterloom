using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Config.Persistence;
using Asterloom.Modules.Outbox;

namespace Asterloom.Modules.Infrastructure.Config;

internal sealed class InMemoryConfigStore(IOutboxStore outboxStore) : IConfigStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ConfigEntry> _entries = [];
    private readonly Dictionary<(Guid EntryId, long Revision), ConfigRevision> _revisions = [];
    private readonly Dictionary<(Guid EnvironmentId, long Version), ConfigSnapshot> _snapshots = [];

    public Task<ConfigStorePage<ConfigEntry>> ListEntriesAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        ConfigPageRequest page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var items = _entries.Values
                .Where(entry =>
                    HasScope(entry, tenantId, applicationId, environmentId)
                    && (page.IncludeArchived || entry.Status == ConfigResourceStatus.Active)
                    && Matches(entry, page.Query))
                .OrderBy(static entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Id)
                .Skip(page.Offset)
                .Take(page.PageSize + 1)
                .ToList();
            var hasMore = items.Count > page.PageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }
            return Task.FromResult(new ConfigStorePage<ConfigEntry>(items, hasMore));
        }
    }

    public Task<ConfigEntry?> GetEntryAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _entries.TryGetValue(entryId, out var entry)
                && HasScope(entry, tenantId, applicationId, environmentId)
                    ? entry
                    : null);
        }
    }

    public Task<ConfigEntry?> GetEntryByKeyAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_entries.Values.FirstOrDefault(entry =>
                HasScope(entry, tenantId, applicationId, environmentId)
                && string.Equals(entry.Key, key, StringComparison.Ordinal)));
        }
    }

    public Task<IReadOnlyList<ConfigEntry>> ListPublishedEntriesAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ConfigEntry>>(_entries.Values
                .Where(entry => HasScope(entry, tenantId, applicationId, environmentId)
                    && entry.PublishedDefinition is not null)
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .ToArray());
        }
    }

    public Task<bool> TryCreateEntryAsync(
        ConfigEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_entries.ContainsKey(entry.Id)
                || _entries.Values.Any(existing =>
                    HasScope(existing, entry.TenantId, entry.ApplicationId, entry.EnvironmentId)
                    && string.Equals(existing.Key, entry.Key, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }
            _entries.Add(entry.Id, entry);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateEntryAsync(
        ConfigEntry entry,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!CanUpdate(entry, expectedVersion))
            {
                return Task.FromResult(false);
            }
            _entries[entry.Id] = entry;
            return Task.FromResult(true);
        }
    }

    public Task<ConfigStorePage<ConfigRevision>> ListRevisionsAsync(
        Guid entryId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var items = _revisions.Values
                .Where(revision => revision.EntryId == entryId)
                .OrderByDescending(static revision => revision.Revision)
                .Skip(offset)
                .Take(pageSize + 1)
                .ToList();
            var hasMore = items.Count > pageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }
            return Task.FromResult(new ConfigStorePage<ConfigRevision>(items, hasMore));
        }
    }

    public Task<ConfigRevision?> GetRevisionAsync(
        Guid entryId,
        long revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_revisions.GetValueOrDefault((entryId, revision)));
        }
    }

    public Task<ConfigSnapshot?> GetLatestSnapshotAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_snapshots.Values
                .Where(snapshot => HasScope(snapshot, tenantId, applicationId, environmentId))
                .OrderByDescending(static snapshot => snapshot.Version)
                .FirstOrDefault());
        }
    }

    public Task<ConfigStorePage<ConfigSnapshot>> ListSnapshotsAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var items = _snapshots.Values
                .Where(snapshot => HasScope(snapshot, tenantId, applicationId, environmentId))
                .OrderByDescending(static snapshot => snapshot.Version)
                .Skip(offset)
                .Take(pageSize + 1)
                .ToList();
            var hasMore = items.Count > pageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }
            return Task.FromResult(new ConfigStorePage<ConfigSnapshot>(items, hasMore));
        }
    }

    public async Task<bool> TryCommitSnapshotAsync(
        ConfigEntry entry,
        long expectedVersion,
        ConfigRevision? revision,
        ConfigSnapshot snapshot,
        OutboxMessageDraft integrationEvent,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var latestVersion = _snapshots.Values
                .Where(candidate => HasScope(
                    candidate,
                    snapshot.TenantId,
                    snapshot.ApplicationId,
                    snapshot.EnvironmentId))
                .Select(static candidate => candidate.Version)
                .DefaultIfEmpty(0)
                .Max();
            if (!CanUpdate(entry, expectedVersion)
                || snapshot.Version != latestVersion + 1
                || _snapshots.ContainsKey((snapshot.EnvironmentId, snapshot.Version))
                || (revision is not null
                    && _revisions.ContainsKey((revision.EntryId, revision.Revision))))
            {
                return false;
            }

            _entries[entry.Id] = entry;
            if (revision is not null)
            {
                _revisions.Add((revision.EntryId, revision.Revision), revision);
            }
            _snapshots.Add((snapshot.EnvironmentId, snapshot.Version), snapshot);
        }

        await outboxStore.EnqueueAsync(integrationEvent, cancellationToken);
        return true;
    }

    private bool CanUpdate(ConfigEntry entry, long expectedVersion) =>
        _entries.TryGetValue(entry.Id, out var current)
        && current.Version == expectedVersion
        && HasScope(current, entry.TenantId, entry.ApplicationId, entry.EnvironmentId)
        && string.Equals(current.Key, entry.Key, StringComparison.Ordinal);

    private static bool HasScope(
        ConfigEntry entry,
        Guid tenantId,
        Guid applicationId,
        Guid environmentId) =>
        entry.TenantId == tenantId
        && entry.ApplicationId == applicationId
        && entry.EnvironmentId == environmentId;

    private static bool HasScope(
        ConfigSnapshot snapshot,
        Guid tenantId,
        Guid applicationId,
        Guid environmentId) =>
        snapshot.TenantId == tenantId
        && snapshot.ApplicationId == applicationId
        && snapshot.EnvironmentId == environmentId;

    private static bool Matches(ConfigEntry entry, string query) =>
        string.IsNullOrEmpty(query)
        || entry.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
}
