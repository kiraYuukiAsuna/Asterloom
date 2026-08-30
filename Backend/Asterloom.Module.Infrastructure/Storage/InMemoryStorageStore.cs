using Asterloom.Modules.Storage.Model;
using Asterloom.Modules.Storage.Persistence;

namespace Asterloom.Modules.Infrastructure.Storage;

internal sealed class InMemoryStorageStore : IStorageStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, StorageBucket> _buckets = [];
    private readonly Dictionary<Guid, StorageObject> _objects = [];
    private readonly Dictionary<Guid, StorageUploadSession> _sessions = [];

    public Task<StorageStorePage<StorageBucket>> ListBucketsAsync(
        Guid tenantId,
        StoragePageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var query = _buckets.Values
                .Where(item => item.TenantId == tenantId)
                .Where(item => request.IncludeInactive
                    || item.Status == StorageResourceStatus.Active)
                .Where(item => Matches(item.Key, item.DisplayName, request.Query))
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Id)
                .Skip(request.Offset)
                .Take(request.Limit + 1)
                .ToArray();
            return Task.FromResult(ToPage(query, request.Limit));
        }
    }

    public Task<StorageBucket?> GetBucketAsync(
        Guid tenantId,
        Guid bucketId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _buckets.TryGetValue(bucketId, out var bucket) && bucket.TenantId == tenantId
                    ? bucket
                    : null);
        }
    }

    public Task<StorageBucket?> GetBucketByKeyAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_buckets.Values.FirstOrDefault(item =>
                item.TenantId == tenantId
                && string.Equals(item.Key, key, StringComparison.Ordinal)));
        }
    }

    public Task<bool> TryCreateBucketAsync(
        StorageBucket bucket,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_buckets.ContainsKey(bucket.Id)
                || _buckets.Values.Any(item => item.TenantId == bucket.TenantId
                    && string.Equals(item.Key, bucket.Key, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }
            _buckets.Add(bucket.Id, bucket);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateBucketAsync(
        StorageBucket bucket,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_buckets.TryGetValue(bucket.Id, out var current)
                || current.TenantId != bucket.TenantId
                || current.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }
            _buckets[bucket.Id] = bucket;
            return Task.FromResult(true);
        }
    }

    public Task<StorageStorePage<StorageObject>> ListObjectsAsync(
        Guid tenantId,
        Guid bucketId,
        StoragePageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var query = _objects.Values
                .Where(item => item.TenantId == tenantId && item.BucketId == bucketId)
                .Where(item => request.IncludeInactive || item.Status != StorageObjectStatus.Deleted)
                .Where(item => Matches(item.ObjectKey, item.FileName, request.Query))
                .OrderByDescending(static item => item.CreatedAt)
                .ThenBy(static item => item.Id)
                .Skip(request.Offset)
                .Take(request.Limit + 1)
                .ToArray();
            return Task.FromResult(ToPage(query, request.Limit));
        }
    }

    public Task<StorageObject?> GetObjectAsync(
        Guid tenantId,
        Guid bucketId,
        Guid objectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _objects.TryGetValue(objectId, out var item)
                && item.TenantId == tenantId
                && item.BucketId == bucketId
                    ? item
                    : null);
        }
    }

    public Task<StorageObject?> GetObjectByKeyAsync(
        Guid tenantId,
        Guid bucketId,
        string objectKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_objects.Values.FirstOrDefault(item =>
                item.TenantId == tenantId
                && item.BucketId == bucketId
                && string.Equals(item.ObjectKey, objectKey, StringComparison.Ordinal)));
        }
    }

    public Task<StorageUploadSession?> GetUploadSessionAsync(
        Guid tenantId,
        Guid bucketId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _sessions.TryGetValue(sessionId, out var session)
                && session.TenantId == tenantId
                && session.BucketId == bucketId
                    ? session
                    : null);
        }
    }

    public Task<bool> TryCreateUploadAsync(
        StorageBucket reservedBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        StorageUploadSession session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!MatchesBucket(reservedBucket, expectedBucketVersion)
                || _objects.ContainsKey(storageObject.Id)
                || _sessions.ContainsKey(session.Id)
                || _objects.Values.Any(item => item.TenantId == storageObject.TenantId
                    && item.BucketId == storageObject.BucketId
                    && string.Equals(item.ObjectKey, storageObject.ObjectKey, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }
            _buckets[reservedBucket.Id] = reservedBucket;
            _objects.Add(storageObject.Id, storageObject);
            _sessions.Add(session.Id, session);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryCompleteUploadAsync(
        StorageBucket completedBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        StorageUploadSession session,
        long expectedSessionVersion,
        CancellationToken cancellationToken) =>
        TryUpdateUploadAsync(
            completedBucket,
            expectedBucketVersion,
            storageObject,
            expectedObjectVersion,
            session,
            expectedSessionVersion,
            cancellationToken);

    public Task<bool> TryFailUploadAsync(
        StorageBucket releasedBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        StorageUploadSession session,
        long expectedSessionVersion,
        CancellationToken cancellationToken) =>
        TryUpdateUploadAsync(
            releasedBucket,
            expectedBucketVersion,
            storageObject,
            expectedObjectVersion,
            session,
            expectedSessionVersion,
            cancellationToken);

    public Task<bool> TryUpdateObjectAsync(
        StorageObject storageObject,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!MatchesObject(storageObject, expectedVersion))
            {
                return Task.FromResult(false);
            }
            _objects[storageObject.Id] = storageObject;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryCopyObjectAsync(
        StorageBucket targetBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!MatchesBucket(targetBucket, expectedBucketVersion)
                || _objects.ContainsKey(storageObject.Id)
                || _objects.Values.Any(item => item.TenantId == storageObject.TenantId
                    && item.BucketId == storageObject.BucketId
                    && string.Equals(item.ObjectKey, storageObject.ObjectKey, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }
            _buckets[targetBucket.Id] = targetBucket;
            _objects.Add(storageObject.Id, storageObject);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryDeleteObjectAsync(
        StorageBucket bucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!MatchesBucket(bucket, expectedBucketVersion)
                || !MatchesObject(storageObject, expectedObjectVersion))
            {
                return Task.FromResult(false);
            }
            _buckets[bucket.Id] = bucket;
            _objects[storageObject.Id] = storageObject;
            return Task.FromResult(true);
        }
    }

    private Task<bool> TryUpdateUploadAsync(
        StorageBucket bucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        StorageUploadSession session,
        long expectedSessionVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!MatchesBucket(bucket, expectedBucketVersion)
                || !MatchesObject(storageObject, expectedObjectVersion)
                || !_sessions.TryGetValue(session.Id, out var currentSession)
                || currentSession.Version != expectedSessionVersion
                || currentSession.TenantId != session.TenantId
                || currentSession.BucketId != session.BucketId)
            {
                return Task.FromResult(false);
            }
            _buckets[bucket.Id] = bucket;
            _objects[storageObject.Id] = storageObject;
            _sessions[session.Id] = session;
            return Task.FromResult(true);
        }
    }

    private bool MatchesBucket(StorageBucket bucket, long expectedVersion) =>
        _buckets.TryGetValue(bucket.Id, out var current)
        && current.TenantId == bucket.TenantId
        && current.Version == expectedVersion;

    private bool MatchesObject(StorageObject storageObject, long expectedVersion) =>
        _objects.TryGetValue(storageObject.Id, out var current)
        && current.TenantId == storageObject.TenantId
        && current.BucketId == storageObject.BucketId
        && current.Version == expectedVersion;

    private static bool Matches(string first, string second, string query) =>
        string.IsNullOrEmpty(query)
        || first.Contains(query, StringComparison.OrdinalIgnoreCase)
        || second.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static StorageStorePage<T> ToPage<T>(IReadOnlyList<T> items, int limit) =>
        new(items.Take(limit).ToArray(), items.Count > limit);
}
