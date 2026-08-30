using Asterloom.Modules.Storage.Model;
using Google.Protobuf.WellKnownTypes;
using ProtocolAccessPolicy = Asterloom.Protocol.Storage.V1.StorageAccessPolicy;
using ProtocolBucket = Asterloom.Protocol.Storage.V1.StorageBucket;
using ProtocolObject = Asterloom.Protocol.Storage.V1.StorageObject;
using ProtocolObjectStatus = Asterloom.Protocol.Storage.V1.StorageObjectStatus;
using ProtocolResourceStatus = Asterloom.Protocol.Storage.V1.StorageResourceStatus;
using ProtocolTicket = Asterloom.Protocol.Storage.V1.StorageTransferTicket;
using ProtocolUploadSession = Asterloom.Protocol.Storage.V1.StorageUploadSession;
using ProtocolUploadStatus = Asterloom.Protocol.Storage.V1.StorageUploadStatus;

namespace Asterloom.Modules.Storage;

public static class StorageProtocolMapper
{
    public static ProtocolBucket ToProtocol(this StorageBucket bucket)
    {
        var result = new ProtocolBucket
        {
            Id = bucket.Id.ToString("D"),
            TenantId = bucket.TenantId.ToString("D"),
            Key = bucket.Key,
            DisplayName = bucket.DisplayName,
            Description = bucket.Description,
            QuotaBytes = bucket.QuotaBytes,
            MaxObjectSizeBytes = bucket.MaxObjectSizeBytes,
            AccessPolicy = bucket.AccessPolicy switch
            {
                StorageAccessPolicy.Private => ProtocolAccessPolicy.Private,
                StorageAccessPolicy.AuthenticatedRead => ProtocolAccessPolicy.AuthenticatedRead,
                _ => ProtocolAccessPolicy.Unspecified,
            },
            Status = bucket.Status switch
            {
                StorageResourceStatus.Active => ProtocolResourceStatus.Active,
                StorageResourceStatus.Archived => ProtocolResourceStatus.Archived,
                _ => ProtocolResourceStatus.Unspecified,
            },
            UsedBytes = bucket.UsedBytes,
            ObjectCount = bucket.ObjectCount,
            Version = bucket.Version,
            CreatedAt = Timestamp.FromDateTimeOffset(bucket.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(bucket.UpdatedAt),
            ArchivedAt = bucket.ArchivedAt is { } archivedAt
                ? Timestamp.FromDateTimeOffset(archivedAt)
                : null,
        };
        result.AllowedContentTypes.AddRange(bucket.AllowedContentTypes);
        return result;
    }

    public static ProtocolObject ToProtocol(this StorageObject storageObject)
    {
        var result = new ProtocolObject
        {
            Id = storageObject.Id.ToString("D"),
            TenantId = storageObject.TenantId.ToString("D"),
            BucketId = storageObject.BucketId.ToString("D"),
            ApplicationId = storageObject.ApplicationId?.ToString("D") ?? string.Empty,
            EnvironmentId = storageObject.EnvironmentId?.ToString("D") ?? string.Empty,
            ObjectKey = storageObject.ObjectKey,
            FileName = storageObject.FileName,
            ContentType = storageObject.ContentType,
            SizeBytes = storageObject.SizeBytes,
            Sha256 = storageObject.Sha256,
            Status = storageObject.Status switch
            {
                StorageObjectStatus.Pending => ProtocolObjectStatus.Pending,
                StorageObjectStatus.Available => ProtocolObjectStatus.Available,
                StorageObjectStatus.Failed => ProtocolObjectStatus.Failed,
                StorageObjectStatus.Deleted => ProtocolObjectStatus.Deleted,
                _ => ProtocolObjectStatus.Unspecified,
            },
            Version = storageObject.Version,
            CreatedAt = Timestamp.FromDateTimeOffset(storageObject.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(storageObject.UpdatedAt),
            CompletedAt = storageObject.CompletedAt is { } completedAt
                ? Timestamp.FromDateTimeOffset(completedAt)
                : null,
            DeletedAt = storageObject.DeletedAt is { } deletedAt
                ? Timestamp.FromDateTimeOffset(deletedAt)
                : null,
        };
        foreach (var pair in storageObject.CustomMetadata)
        {
            result.CustomMetadata.Add(pair.Key, pair.Value);
        }
        return result;
    }

    public static ProtocolTicket ToProtocol(this StorageTransferTicket ticket)
    {
        var result = new ProtocolTicket
        {
            Url = ticket.Url,
            Method = ticket.Method,
            ExpiresAt = Timestamp.FromDateTimeOffset(ticket.ExpiresAt),
        };
        foreach (var pair in ticket.RequiredHeaders)
        {
            result.RequiredHeaders.Add(pair.Key, pair.Value);
        }
        return result;
    }

    public static ProtocolUploadSession ToProtocol(this StorageUploadSessionDetails details)
    {
        var session = details.Session;
        return new ProtocolUploadSession
        {
            Id = session.Id.ToString("D"),
            TenantId = session.TenantId.ToString("D"),
            BucketId = session.BucketId.ToString("D"),
            Object = details.StorageObject.ToProtocol(),
            Transfer = session.Transfer.ToProtocol(),
            Status = session.Status switch
            {
                StorageUploadStatus.Pending => ProtocolUploadStatus.Pending,
                StorageUploadStatus.Uploaded => ProtocolUploadStatus.Uploaded,
                StorageUploadStatus.Completed => ProtocolUploadStatus.Completed,
                StorageUploadStatus.Failed => ProtocolUploadStatus.Failed,
                StorageUploadStatus.Expired => ProtocolUploadStatus.Expired,
                _ => ProtocolUploadStatus.Unspecified,
            },
            FailureReason = session.FailureReason,
            Version = session.Version,
            CreatedAt = Timestamp.FromDateTimeOffset(session.CreatedAt),
            ExpiresAt = Timestamp.FromDateTimeOffset(session.ExpiresAt),
            CompletedAt = session.CompletedAt is { } completedAt
                ? Timestamp.FromDateTimeOffset(completedAt)
                : null,
        };
    }

    public static StorageAccessPolicy ToDomain(
        this Asterloom.Protocol.Storage.V1.StorageAccessPolicy policy) => policy switch
    {
        Asterloom.Protocol.Storage.V1.StorageAccessPolicy.Private => StorageAccessPolicy.Private,
        Asterloom.Protocol.Storage.V1.StorageAccessPolicy.AuthenticatedRead => StorageAccessPolicy.AuthenticatedRead,
        _ => (StorageAccessPolicy)0,
    };
}
