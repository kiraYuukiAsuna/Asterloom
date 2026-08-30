namespace Asterloom.Modules.Storage.Model;

public enum StorageResourceStatus : short
{
    Active = 1,
    Archived = 2,
}

public enum StorageAccessPolicy : short
{
    Private = 1,
    AuthenticatedRead = 2,
}

public enum StorageObjectStatus : short
{
    Pending = 1,
    Available = 2,
    Failed = 3,
    Deleted = 4,
}

public enum StorageUploadStatus : short
{
    Pending = 1,
    Uploaded = 2,
    Completed = 3,
    Failed = 4,
    Expired = 5,
}

public sealed record StorageBucket(
    Guid Id,
    Guid TenantId,
    string Key,
    string DisplayName,
    string Description,
    long QuotaBytes,
    long MaxObjectSizeBytes,
    IReadOnlyList<string> AllowedContentTypes,
    StorageAccessPolicy AccessPolicy,
    StorageResourceStatus Status,
    long UsedBytes,
    long ReservedBytes,
    long ObjectCount,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record StorageObject(
    Guid Id,
    Guid TenantId,
    Guid BucketId,
    Guid? ApplicationId,
    Guid? EnvironmentId,
    string ObjectKey,
    string PhysicalKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    IReadOnlyDictionary<string, string> CustomMetadata,
    StorageObjectStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DeletedAt);

public sealed record StorageTransferTicket(
    string Url,
    string Method,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);

public sealed record StorageUploadSession(
    Guid Id,
    Guid TenantId,
    Guid BucketId,
    Guid ObjectId,
    StorageTransferTicket Transfer,
    StorageUploadStatus Status,
    string FailureReason,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt);

public sealed record StorageUploadSessionDetails(
    StorageUploadSession Session,
    StorageObject StorageObject);

public sealed record StoragePageRequest(
    int Offset,
    int Limit,
    string Query,
    bool IncludeInactive);

public sealed record StorageStorePage<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed record StorageListResult<T>(IReadOnlyList<T> Items, string NextPageToken);

public sealed record StorageObjectDescriptor(
    string PhysicalKey,
    string ContentType,
    long SizeBytes,
    string Sha256,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record StoredObjectInspection(
    bool Exists,
    long SizeBytes,
    string Sha256,
    string ContentType);

public sealed record LocalObjectDownload(
    Stream Content,
    string ContentType,
    long SizeBytes,
    string FileName);
