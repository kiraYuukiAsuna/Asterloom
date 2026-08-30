using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Asterloom.Modules.Storage.Model;
using Asterloom.Modules.Storage.Persistence;
using Asterloom.Modules.Storage.Transport;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Storage;

public sealed partial class StorageManagementService(
    IStorageStore store,
    IObjectStorageTransport transport,
    IPlatformResourceStore platformStore,
    TimeProvider timeProvider)
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const long DefaultQuotaBytes = 10L * 1024 * 1024 * 1024;
    private const long DefaultMaxObjectSizeBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(15);

    public async Task<StorageListResult<StorageBucket>> ListBucketsAsync(
        string tenantId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: false, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        return ToListResult(
            await store.ListBucketsAsync(tenant, page, cancellationToken),
            page.Offset);
    }

    public async Task<StorageBucket> GetBucketAsync(
        string tenantId,
        string bucketId,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: false, cancellationToken);
        return await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
    }

    public async Task<StorageBucket> CreateBucketAsync(
        string tenantId,
        string key,
        string displayName,
        string? description,
        long quotaBytes,
        long maxObjectSizeBytes,
        IReadOnlyList<string> allowedContentTypes,
        StorageAccessPolicy accessPolicy,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: true, cancellationToken);
        var normalizedKey = NormalizeKey(key, "key", BucketKeyPattern());
        var limits = NormalizeLimits(quotaBytes, maxObjectSizeBytes, 0);
        var now = timeProvider.GetUtcNow();
        var bucket = new StorageBucket(
            Guid.CreateVersion7(),
            tenant,
            normalizedKey,
            NormalizeDisplayName(displayName),
            NormalizeDescription(description),
            limits.QuotaBytes,
            limits.MaxObjectSizeBytes,
            NormalizeContentTypes(allowedContentTypes),
            NormalizeAccessPolicy(accessPolicy),
            StorageResourceStatus.Active,
            UsedBytes: 0,
            ReservedBytes: 0,
            ObjectCount: 0,
            Version: 1,
            now,
            now,
            ArchivedAt: null);
        if (!await store.TryCreateBucketAsync(bucket, cancellationToken))
        {
            throw AlreadyExists("storage_bucket_key_exists", "A bucket with this key already exists.");
        }
        return bucket;
    }

    public async Task<StorageBucket> UpdateBucketAsync(
        string tenantId,
        string bucketId,
        string displayName,
        string? description,
        long quotaBytes,
        long maxObjectSizeBytes,
        IReadOnlyList<string> allowedContentTypes,
        StorageAccessPolicy accessPolicy,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: true, cancellationToken);
        var current = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current);
        var limits = NormalizeLimits(
            quotaBytes,
            maxObjectSizeBytes,
            checked(current.UsedBytes + current.ReservedBytes));
        var updated = current with
        {
            DisplayName = NormalizeDisplayName(displayName),
            Description = NormalizeDescription(description),
            QuotaBytes = limits.QuotaBytes,
            MaxObjectSizeBytes = limits.MaxObjectSizeBytes,
            AllowedContentTypes = NormalizeContentTypes(allowedContentTypes),
            AccessPolicy = NormalizeAccessPolicy(accessPolicy),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        await PersistBucketAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    public async Task<StorageBucket> ArchiveBucketAsync(
        string tenantId,
        string bucketId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: false, cancellationToken);
        var current = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current);
        var objects = await store.ListObjectsAsync(
            tenant,
            current.Id,
            new StoragePageRequest(0, 1, string.Empty, IncludeInactive: false),
            cancellationToken);
        if (objects.Items.Count > 0 || current.ReservedBytes > 0 || current.ObjectCount > 0)
        {
            throw FailedPrecondition(
                "storage_bucket_not_empty",
                "A bucket must have no pending or available objects before it can be archived.");
        }
        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = StorageResourceStatus.Archived,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = now,
        };
        await PersistBucketAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    public async Task<StorageBucket> RestoreBucketAsync(
        string tenantId,
        string bucketId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: true, cancellationToken);
        var current = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status != StorageResourceStatus.Archived)
        {
            throw FailedPrecondition("storage_bucket_active", "The bucket is already active.");
        }
        var updated = current with
        {
            Status = StorageResourceStatus.Active,
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
            ArchivedAt = null,
        };
        await PersistBucketAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    public async Task<StorageListResult<StorageObject>> ListObjectsAsync(
        string tenantId,
        string bucketId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: false, cancellationToken);
        var bucket = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query, includeDeleted);
        return ToListResult(
            await store.ListObjectsAsync(tenant, bucket.Id, page, cancellationToken),
            page.Offset);
    }

    public async Task<StorageObject> GetObjectAsync(
        string tenantId,
        string bucketId,
        string objectId,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: false, cancellationToken);
        var bucket = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        return await RequireObjectAsync(
            tenant,
            bucket.Id,
            ParseId(objectId, "objectId"),
            cancellationToken);
    }

    public async Task<StorageObject> UpdateObjectMetadataAsync(
        string tenantId,
        string bucketId,
        string objectId,
        string fileName,
        IReadOnlyDictionary<string, string> customMetadata,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: true, cancellationToken);
        var bucket = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        RequireActive(bucket);
        var current = await RequireObjectAsync(
            tenant,
            bucket.Id,
            ParseId(objectId, "objectId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireAvailable(current);
        var updated = current with
        {
            FileName = NormalizeFileName(fileName),
            CustomMetadata = NormalizeMetadata(customMetadata),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        if (!await store.TryUpdateObjectAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }
        return updated;
    }

    public async Task<StorageUploadSessionDetails> CreateUploadSessionAsync(
        string tenantId,
        string bucketId,
        string? applicationId,
        string? environmentId,
        string objectKey,
        string fileName,
        string contentType,
        long sizeBytes,
        string sha256,
        IReadOnlyDictionary<string, string> customMetadata,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: true, cancellationToken);
        var bucket = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        RequireActive(bucket);
        var ownership = await ParseAndRequireOwnershipAsync(
            tenant,
            applicationId,
            environmentId,
            cancellationToken);
        var normalizedKey = NormalizeObjectKey(objectKey);
        var normalizedType = NormalizeContentType(contentType);
        RequireContentType(bucket, normalizedType);
        if (sizeBytes <= 0 || sizeBytes > bucket.MaxObjectSizeBytes)
        {
            throw Invalid(
                "sizeBytes",
                $"Object size must be between 1 and {bucket.MaxObjectSizeBytes} bytes.");
        }
        if (checked(bucket.UsedBytes + bucket.ReservedBytes + sizeBytes) > bucket.QuotaBytes)
        {
            throw FailedPrecondition("storage_quota_exceeded", "The bucket quota would be exceeded.");
        }
        if (await store.GetObjectByKeyAsync(tenant, bucket.Id, normalizedKey, cancellationToken) is not null)
        {
            throw AlreadyExists("storage_object_key_exists", "An object with this key already exists.");
        }

        var now = timeProvider.GetUtcNow();
        var sessionId = Guid.CreateVersion7();
        var objectId = Guid.CreateVersion7();
        var physicalKey = $"tenants/{tenant:N}/buckets/{bucket.Id:N}/objects/{objectId:N}";
        var descriptor = new StorageObjectDescriptor(
            physicalKey,
            normalizedType,
            sizeBytes,
            NormalizeSha256(sha256),
            NormalizeMetadata(customMetadata));
        var expiresAt = now.Add(UploadLifetime);
        var transfer = await transport.CreateUploadTicketAsync(
            sessionId,
            descriptor,
            expiresAt,
            cancellationToken);
        var storageObject = new StorageObject(
            objectId,
            tenant,
            bucket.Id,
            ownership.ApplicationId,
            ownership.EnvironmentId,
            normalizedKey,
            physicalKey,
            NormalizeFileName(fileName),
            normalizedType,
            sizeBytes,
            descriptor.Sha256,
            descriptor.Metadata,
            StorageObjectStatus.Pending,
            Version: 1,
            now,
            now,
            CompletedAt: null,
            DeletedAt: null);
        var session = new StorageUploadSession(
            sessionId,
            tenant,
            bucket.Id,
            objectId,
            transfer,
            StorageUploadStatus.Pending,
            string.Empty,
            Version: 1,
            now,
            expiresAt,
            CompletedAt: null);
        var reservedBucket = bucket with
        {
            ReservedBytes = checked(bucket.ReservedBytes + sizeBytes),
            Version = bucket.Version + 1,
            UpdatedAt = now,
        };
        if (!await store.TryCreateUploadAsync(
                reservedBucket,
                bucket.Version,
                storageObject,
                session,
                cancellationToken))
        {
            throw VersionConflict();
        }
        return new(session, storageObject);
    }

    public async Task<StorageObject> CompleteUploadAsync(
        string tenantId,
        string bucketId,
        string uploadSessionId,
        long expectedObjectVersion,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        var bucket = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        RequireActive(bucket);
        var session = await store.GetUploadSessionAsync(
            tenant,
            bucket.Id,
            ParseId(uploadSessionId, "uploadSessionId"),
            cancellationToken)
            ?? throw NotFound("storage_upload_session_not_found", "The upload session was not found.");
        var current = await RequireObjectAsync(tenant, bucket.Id, session.ObjectId, cancellationToken);
        RequireVersion(current.Version, expectedObjectVersion);
        if (session.Status == StorageUploadStatus.Completed)
        {
            return current;
        }
        if (session.Status != StorageUploadStatus.Pending || current.Status != StorageObjectStatus.Pending)
        {
            throw FailedPrecondition("storage_upload_not_pending", "The upload session is no longer pending.");
        }

        var now = timeProvider.GetUtcNow();
        if (session.ExpiresAt <= now)
        {
            await FailUploadAsync(bucket, current, session, "upload_session_expired", now, cancellationToken);
            throw FailedPrecondition("storage_upload_expired", "The upload session has expired.");
        }
        var descriptor = ToDescriptor(current);
        var inspection = await transport.InspectAsync(descriptor, cancellationToken);
        if (!inspection.Exists
            || inspection.SizeBytes != current.SizeBytes
            || !string.Equals(inspection.Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(inspection.ContentType, current.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            await transport.DeleteAsync(current.PhysicalKey, cancellationToken);
            await FailUploadAsync(bucket, current, session, "object_integrity_mismatch", now, cancellationToken);
            throw FailedPrecondition(
                "storage_upload_integrity_mismatch",
                "The uploaded object does not match the declared size, content type, and SHA-256 hash.");
        }

        var completedObject = current with
        {
            Status = StorageObjectStatus.Available,
            Version = current.Version + 1,
            UpdatedAt = now,
            CompletedAt = now,
        };
        var completedSession = session with
        {
            Status = StorageUploadStatus.Completed,
            Version = session.Version + 1,
            CompletedAt = now,
        };
        var completedBucket = bucket with
        {
            ReservedBytes = checked(bucket.ReservedBytes - current.SizeBytes),
            UsedBytes = checked(bucket.UsedBytes + current.SizeBytes),
            ObjectCount = checked(bucket.ObjectCount + 1),
            Version = bucket.Version + 1,
            UpdatedAt = now,
        };
        if (!await store.TryCompleteUploadAsync(
                completedBucket,
                bucket.Version,
                completedObject,
                current.Version,
                completedSession,
                session.Version,
                cancellationToken))
        {
            throw VersionConflict();
        }
        return completedObject;
    }

    public async Task<StorageTransferTicket> CreateDownloadUrlAsync(
        string tenantId,
        string bucketId,
        string objectId,
        int lifetimeSeconds,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: false, cancellationToken);
        var bucket = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        var storageObject = await RequireObjectAsync(
            tenant,
            bucket.Id,
            ParseId(objectId, "objectId"),
            cancellationToken);
        RequireAvailable(storageObject);
        var lifetime = lifetimeSeconds == 0 ? 300 : lifetimeSeconds;
        if (lifetime is < 30 or > 900)
        {
            throw Invalid("lifetimeSeconds", "Download URL lifetime must be between 30 and 900 seconds.");
        }
        return await transport.CreateDownloadTicketAsync(
            Guid.CreateVersion7(),
            ToDescriptor(storageObject),
            storageObject.FileName,
            timeProvider.GetUtcNow().AddSeconds(lifetime),
            cancellationToken);
    }

    public async Task<StorageObject> CopyObjectAsync(
        string tenantId,
        string sourceBucketId,
        string objectId,
        string targetBucketId,
        string objectKey,
        string fileName,
        IReadOnlyDictionary<string, string> customMetadata,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: true, cancellationToken);
        var sourceBucket = await RequireBucketAsync(
            tenant,
            ParseId(sourceBucketId, "bucketId"),
            cancellationToken);
        var source = await RequireObjectAsync(
            tenant,
            sourceBucket.Id,
            ParseId(objectId, "objectId"),
            cancellationToken);
        RequireAvailable(source);
        var target = await RequireBucketAsync(
            tenant,
            ParseId(targetBucketId, "targetBucketId"),
            cancellationToken);
        RequireActive(target);
        RequireContentType(target, source.ContentType);
        if (source.SizeBytes > target.MaxObjectSizeBytes
            || checked(target.UsedBytes + target.ReservedBytes + source.SizeBytes) > target.QuotaBytes)
        {
            throw FailedPrecondition(
                "storage_copy_quota_exceeded",
                "The target bucket cannot accept this object because of its size or quota.");
        }
        var normalizedKey = NormalizeObjectKey(objectKey);
        if (await store.GetObjectByKeyAsync(tenant, target.Id, normalizedKey, cancellationToken) is not null)
        {
            throw AlreadyExists("storage_object_key_exists", "An object with this key already exists.");
        }
        var now = timeProvider.GetUtcNow();
        var copyId = Guid.CreateVersion7();
        var physicalKey = $"tenants/{tenant:N}/buckets/{target.Id:N}/objects/{copyId:N}";
        var metadata = NormalizeMetadata(customMetadata);
        await transport.CopyAsync(source.PhysicalKey, physicalKey, metadata, cancellationToken);
        var copied = source with
        {
            Id = copyId,
            BucketId = target.Id,
            ObjectKey = normalizedKey,
            PhysicalKey = physicalKey,
            FileName = NormalizeFileName(fileName),
            CustomMetadata = metadata,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = now,
            DeletedAt = null,
        };
        var updatedTarget = target with
        {
            UsedBytes = checked(target.UsedBytes + copied.SizeBytes),
            ObjectCount = checked(target.ObjectCount + 1),
            Version = target.Version + 1,
            UpdatedAt = now,
        };
        if (!await store.TryCopyObjectAsync(updatedTarget, target.Version, copied, cancellationToken))
        {
            await transport.DeleteAsync(physicalKey, CancellationToken.None);
            throw VersionConflict();
        }
        return copied;
    }

    public async Task<StorageObject> DeleteObjectAsync(
        string tenantId,
        string bucketId,
        string objectId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: true, cancellationToken);
        var bucket = await RequireBucketAsync(tenant, ParseId(bucketId, "bucketId"), cancellationToken);
        RequireActive(bucket);
        var current = await RequireObjectAsync(
            tenant,
            bucket.Id,
            ParseId(objectId, "objectId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireAvailable(current);
        await transport.DeleteAsync(current.PhysicalKey, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var deleted = current with
        {
            Status = StorageObjectStatus.Deleted,
            Version = current.Version + 1,
            UpdatedAt = now,
            DeletedAt = now,
        };
        var updatedBucket = bucket with
        {
            UsedBytes = checked(bucket.UsedBytes - current.SizeBytes),
            ObjectCount = checked(bucket.ObjectCount - 1),
            Version = bucket.Version + 1,
            UpdatedAt = now,
        };
        if (!await store.TryDeleteObjectAsync(
                updatedBucket,
                bucket.Version,
                deleted,
                current.Version,
                cancellationToken))
        {
            throw VersionConflict();
        }
        return deleted;
    }

    public async Task<StorageBucket> EnsureSystemBucketAsync(
        Guid tenantId,
        string key,
        string displayName,
        long quotaBytes,
        long maxObjectSizeBytes,
        IReadOnlyList<string> contentTypes,
        CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeKey(key, "key", BucketKeyPattern());
        var existing = await store.GetBucketByKeyAsync(tenantId, normalizedKey, cancellationToken);
        if (existing is not null)
        {
            RequireActive(existing);
            return existing;
        }
        try
        {
            return await CreateBucketAsync(
                tenantId.ToString("D"),
                normalizedKey,
                displayName,
                "Asterloom-managed system bucket.",
                quotaBytes,
                maxObjectSizeBytes,
                contentTypes,
                StorageAccessPolicy.Private,
                cancellationToken);
        }
        catch (AsterloomException exception) when (exception.ErrorCode == "storage_bucket_key_exists")
        {
            var raced = await store.GetBucketByKeyAsync(
                tenantId,
                normalizedKey,
                cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    private async Task FailUploadAsync(
        StorageBucket bucket,
        StorageObject storageObject,
        StorageUploadSession session,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var releasedBucket = bucket with
        {
            ReservedBytes = Math.Max(0, bucket.ReservedBytes - storageObject.SizeBytes),
            Version = bucket.Version + 1,
            UpdatedAt = now,
        };
        var failedObject = storageObject with
        {
            Status = StorageObjectStatus.Failed,
            Version = storageObject.Version + 1,
            UpdatedAt = now,
        };
        var failedSession = session with
        {
            Status = session.ExpiresAt <= now
                ? StorageUploadStatus.Expired
                : StorageUploadStatus.Failed,
            FailureReason = reason,
            Version = session.Version + 1,
            CompletedAt = now,
        };
        if (!await store.TryFailUploadAsync(
                releasedBucket,
                bucket.Version,
                failedObject,
                storageObject.Version,
                failedSession,
                session.Version,
                cancellationToken))
        {
            throw VersionConflict();
        }
    }

    private async Task RequireTenantAsync(
        Guid tenantId,
        bool requireActive,
        CancellationToken cancellationToken)
    {
        var tenant = await platformStore.GetTenantAsync(tenantId, cancellationToken)
            ?? throw NotFound("tenant_not_found", "The tenant was not found.");
        if (requireActive && tenant.Status != PlatformResourceStatus.Active)
        {
            throw FailedPrecondition("storage_tenant_archived", "The tenant must be active.");
        }
    }

    private async Task<(Guid? ApplicationId, Guid? EnvironmentId)> ParseAndRequireOwnershipAsync(
        Guid tenantId,
        string? applicationId,
        string? environmentId,
        CancellationToken cancellationToken)
    {
        var application = ParseOptionalId(applicationId, "applicationId");
        var environment = ParseOptionalId(environmentId, "environmentId");
        if (environment is not null && application is null)
        {
            throw Invalid("applicationId", "Application is required when environment is provided.");
        }
        if (application is not null)
        {
            var app = await platformStore.GetApplicationAsync(tenantId, application.Value, cancellationToken)
                ?? throw NotFound("application_not_found", "The application was not found.");
            if (app.Status != PlatformResourceStatus.Active)
            {
                throw FailedPrecondition("storage_application_archived", "The application must be active.");
            }
        }
        if (environment is not null)
        {
            var env = await platformStore.GetEnvironmentAsync(
                tenantId,
                application!.Value,
                environment.Value,
                cancellationToken)
                ?? throw NotFound("environment_not_found", "The environment was not found.");
            if (env.Status != PlatformResourceStatus.Active)
            {
                throw FailedPrecondition("storage_environment_archived", "The environment must be active.");
            }
        }
        return (application, environment);
    }

    private async Task<StorageBucket> RequireBucketAsync(
        Guid tenantId,
        Guid bucketId,
        CancellationToken cancellationToken) =>
        await store.GetBucketAsync(tenantId, bucketId, cancellationToken)
        ?? throw NotFound("storage_bucket_not_found", "The bucket was not found.");

    private async Task<StorageObject> RequireObjectAsync(
        Guid tenantId,
        Guid bucketId,
        Guid objectId,
        CancellationToken cancellationToken) =>
        await store.GetObjectAsync(tenantId, bucketId, objectId, cancellationToken)
        ?? throw NotFound("storage_object_not_found", "The object was not found.");

    private async Task PersistBucketAsync(
        StorageBucket bucket,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!await store.TryUpdateBucketAsync(bucket, expectedVersion, cancellationToken))
        {
            throw VersionConflict();
        }
    }

    private static StorageObjectDescriptor ToDescriptor(StorageObject storageObject) => new(
        storageObject.PhysicalKey,
        storageObject.ContentType,
        storageObject.SizeBytes,
        storageObject.Sha256,
        storageObject.CustomMetadata);

    private static Guid ParseId(string value, string field) =>
        ParseOptionalId(value, field) ?? throw Invalid(field, "A valid identifier is required.");

    private static Guid? ParseOptionalId(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw Invalid(field, "A valid identifier is required.");
    }

    private static string NormalizeKey(string? value, string field, Regex pattern)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!pattern.IsMatch(normalized))
        {
            throw Invalid(field, "Use 1-100 lowercase letters, numbers, periods, underscores, or hyphens.");
        }
        return normalized;
    }

    private static string NormalizeObjectKey(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant().Replace('\\', '/') ?? string.Empty;
        if (!ObjectKeyPattern().IsMatch(normalized)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains("//", StringComparison.Ordinal))
        {
            throw Invalid(
                "objectKey",
                "Use 1-512 lowercase letters, numbers, periods, underscores, hyphens, or path separators without empty or parent segments.");
        }
        return normalized;
    }

    private static string NormalizeDisplayName(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 200 || normalized.Any(char.IsControl))
        {
            throw Invalid("displayName", "Display name must contain 1-200 characters without control characters.");
        }
        return normalized;
    }

    private static string NormalizeDescription(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 1_000 || normalized.Any(char.IsControl))
        {
            throw Invalid("description", "Description must not exceed 1000 characters or contain control characters.");
        }
        return normalized;
    }

    private static string NormalizeFileName(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 255
            || normalized.Any(char.IsControl)
            || normalized.IndexOfAny(['/', '\\']) >= 0
            || normalized is "." or "..")
        {
            throw Invalid("fileName", "File name must contain 1-255 safe characters and no path separators.");
        }
        return normalized;
    }

    private static string NormalizeContentType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ContentTypePattern().IsMatch(normalized))
        {
            throw Invalid("contentType", "A valid media type is required.");
        }
        return normalized;
    }

    private static string[] NormalizeContentTypes(IReadOnlyList<string> values)
    {
        if (values.Count > 32)
        {
            throw Invalid("allowedContentTypes", "At most 32 content types are allowed.");
        }
        var result = values
            .Select(static value => value.Trim().ToLowerInvariant())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (result.Any(value => value != "*/*"
            && !(value.EndsWith("/*", StringComparison.Ordinal)
                ? ContentTypePrefixPattern().IsMatch(value)
                : ContentTypePattern().IsMatch(value))))
        {
            throw Invalid("allowedContentTypes", "Every item must be a valid media type or type wildcard.");
        }
        return result;
    }

    private static void RequireContentType(StorageBucket bucket, string contentType)
    {
        if (bucket.AllowedContentTypes.Count == 0
            || bucket.AllowedContentTypes.Contains("*/*", StringComparer.Ordinal)
            || bucket.AllowedContentTypes.Any(allowed =>
                string.Equals(allowed, contentType, StringComparison.Ordinal)
                || (allowed.EndsWith("/*", StringComparison.Ordinal)
                    && contentType.StartsWith(allowed[..^1], StringComparison.Ordinal))))
        {
            return;
        }
        throw FailedPrecondition(
            "storage_content_type_not_allowed",
            "The bucket policy does not allow this content type.");
    }

    private static Dictionary<string, string> NormalizeMetadata(
        IReadOnlyDictionary<string, string> values)
    {
        if (values.Count > 32)
        {
            throw Invalid("customMetadata", "At most 32 metadata entries are allowed.");
        }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            var key = pair.Key.Trim().ToLowerInvariant();
            var value = pair.Value.Trim();
            if (!MetadataKeyPattern().IsMatch(key))
            {
                throw Invalid("customMetadata", "Metadata keys must use lowercase letters, numbers, periods, underscores, or hyphens.");
            }
            if (value.Length > 1_000 || value.Any(char.IsControl))
            {
                throw Invalid("customMetadata", "Metadata values must not exceed 1000 characters or contain control characters.");
            }
            result[key] = value;
        }
        return result;
    }

    private static string NormalizeSha256(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Sha256Pattern().IsMatch(normalized))
        {
            throw Invalid("sha256", "SHA-256 must be a 64-character hexadecimal digest.");
        }
        return normalized;
    }

    private static StorageAccessPolicy NormalizeAccessPolicy(StorageAccessPolicy value)
    {
        if (!Enum.IsDefined(value))
        {
            throw Invalid("accessPolicy", "A supported access policy is required.");
        }
        return value;
    }

    private static (long QuotaBytes, long MaxObjectSizeBytes) NormalizeLimits(
        long quotaBytes,
        long maxObjectSizeBytes,
        long allocatedBytes)
    {
        var quota = quotaBytes == 0 ? DefaultQuotaBytes : quotaBytes;
        var maximum = maxObjectSizeBytes == 0 ? Math.Min(DefaultMaxObjectSizeBytes, quota) : maxObjectSizeBytes;
        if (quota <= 0 || maximum <= 0 || maximum > quota)
        {
            throw Invalid("quotaBytes", "Quota and maximum object size must be positive, and maximum object size cannot exceed quota.");
        }
        if (quota < allocatedBytes)
        {
            throw FailedPrecondition("storage_quota_below_usage", "Quota cannot be lower than current usage and reservations.");
        }
        return (quota, maximum);
    }

    private static void RequireVersion(long currentVersion, long expectedVersion)
    {
        if (expectedVersion <= 0)
        {
            throw Invalid("expectedVersion", "Expected version must be positive.");
        }
        if (currentVersion != expectedVersion)
        {
            throw VersionConflict();
        }
    }

    private static void RequireActive(StorageBucket bucket)
    {
        if (bucket.Status != StorageResourceStatus.Active)
        {
            throw FailedPrecondition("storage_bucket_archived", "The bucket is archived and must be restored first.");
        }
    }

    private static void RequireAvailable(StorageObject storageObject)
    {
        if (storageObject.Status != StorageObjectStatus.Available)
        {
            throw FailedPrecondition("storage_object_unavailable", "The object is not available.");
        }
    }

    private static StoragePageRequest CreatePageRequest(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeInactive)
    {
        var normalizedSize = pageSize == 0 ? DefaultPageSize : pageSize;
        if (normalizedSize is < 1 or > MaximumPageSize)
        {
            throw Invalid("pageSize", $"Page size must be between 1 and {MaximumPageSize}.");
        }
        var offset = 0;
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(pageToken));
                if (!int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0)
                {
                    throw new FormatException();
                }
            }
            catch (FormatException)
            {
                throw Invalid("pageToken", "Page token is invalid.");
            }
        }
        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length > 200 || normalizedQuery.Any(char.IsControl))
        {
            throw Invalid("query", "Query must not exceed 200 characters or contain control characters.");
        }
        return new(offset, normalizedSize, normalizedQuery, includeInactive);
    }

    private static StorageListResult<T> ToListResult<T>(StorageStorePage<T> page, int offset) => new(
        page.Items,
        page.HasMore
            ? WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
                (offset + page.Items.Count).ToString(CultureInfo.InvariantCulture)))
            : string.Empty);

    private static AsterloomException Invalid(string field, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "validation_failed",
        "One or more fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [field] = [message] });

    private static AsterloomException NotFound(string code, string message) =>
        new(AsterloomErrorKind.NotFound, code, message);

    private static AsterloomException AlreadyExists(string code, string message) =>
        new(AsterloomErrorKind.AlreadyExists, code, message);

    private static AsterloomException FailedPrecondition(string code, string message) =>
        new(AsterloomErrorKind.FailedPrecondition, code, message);

    private static AsterloomException VersionConflict() => new(
        AsterloomErrorKind.Conflict,
        "version_conflict",
        "The resource changed after it was loaded. Reload it and try again.");

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$")]
    private static partial Regex BucketKeyPattern();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._/-]{0,510}[a-z0-9])?$")]
    private static partial Regex ObjectKeyPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,62}$")]
    private static partial Regex MetadataKeyPattern();

    [GeneratedRegex("^[a-z0-9!#$&^_.+-]+/[a-z0-9!#$&^_.+-]+$")]
    private static partial Regex ContentTypePattern();

    [GeneratedRegex("^[a-z0-9!#$&^_.+-]+/\\*$")]
    private static partial Regex ContentTypePrefixPattern();

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex Sha256Pattern();
}
