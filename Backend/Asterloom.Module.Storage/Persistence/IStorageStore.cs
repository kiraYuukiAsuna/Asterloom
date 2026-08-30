using Asterloom.Modules.Storage.Model;

namespace Asterloom.Modules.Storage.Persistence;

public interface IStorageStore
{
    Task<StorageStorePage<StorageBucket>> ListBucketsAsync(
        Guid tenantId,
        StoragePageRequest request,
        CancellationToken cancellationToken);

    Task<StorageBucket?> GetBucketAsync(
        Guid tenantId,
        Guid bucketId,
        CancellationToken cancellationToken);

    Task<StorageBucket?> GetBucketByKeyAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken);

    Task<bool> TryCreateBucketAsync(StorageBucket bucket, CancellationToken cancellationToken);

    Task<bool> TryUpdateBucketAsync(
        StorageBucket bucket,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<StorageStorePage<StorageObject>> ListObjectsAsync(
        Guid tenantId,
        Guid bucketId,
        StoragePageRequest request,
        CancellationToken cancellationToken);

    Task<StorageObject?> GetObjectAsync(
        Guid tenantId,
        Guid bucketId,
        Guid objectId,
        CancellationToken cancellationToken);

    Task<StorageObject?> GetObjectByKeyAsync(
        Guid tenantId,
        Guid bucketId,
        string objectKey,
        CancellationToken cancellationToken);

    Task<StorageUploadSession?> GetUploadSessionAsync(
        Guid tenantId,
        Guid bucketId,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateUploadAsync(
        StorageBucket reservedBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        StorageUploadSession session,
        CancellationToken cancellationToken);

    Task<bool> TryCompleteUploadAsync(
        StorageBucket completedBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        StorageUploadSession session,
        long expectedSessionVersion,
        CancellationToken cancellationToken);

    Task<bool> TryFailUploadAsync(
        StorageBucket releasedBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        StorageUploadSession session,
        long expectedSessionVersion,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateObjectAsync(
        StorageObject storageObject,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<bool> TryCopyObjectAsync(
        StorageBucket targetBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        CancellationToken cancellationToken);

    Task<bool> TryDeleteObjectAsync(
        StorageBucket bucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        CancellationToken cancellationToken);
}
