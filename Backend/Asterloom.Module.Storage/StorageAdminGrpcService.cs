using Asterloom.Protocol.Storage.Admin.V1;
using Grpc.Core;
using ProtocolBucket = Asterloom.Protocol.Storage.V1.StorageBucket;
using ProtocolObject = Asterloom.Protocol.Storage.V1.StorageObject;
using ProtocolTicket = Asterloom.Protocol.Storage.V1.StorageTransferTicket;
using ProtocolUploadSession = Asterloom.Protocol.Storage.V1.StorageUploadSession;

namespace Asterloom.Modules.Storage;

public sealed class StorageAdminGrpcService(StorageManagementService managementService)
    : StorageAdminService.StorageAdminServiceBase
{
    public override async Task<ListBucketsResponse> ListBuckets(
        ListBucketsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListBucketsAsync(
            request.TenantId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListBucketsResponse { NextPageToken = result.NextPageToken };
        response.Buckets.AddRange(result.Items.Select(StorageProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolBucket> GetBucket(
        GetBucketRequest request,
        ServerCallContext context) =>
        (await managementService.GetBucketAsync(
            request.TenantId,
            request.BucketId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolBucket> CreateBucket(
        CreateBucketRequest request,
        ServerCallContext context) =>
        (await managementService.CreateBucketAsync(
            request.TenantId,
            request.Key,
            request.DisplayName,
            request.Description,
            request.QuotaBytes,
            request.MaxObjectSizeBytes,
            request.AllowedContentTypes,
            request.AccessPolicy.ToDomain(),
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolBucket> UpdateBucket(
        UpdateBucketRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateBucketAsync(
            request.TenantId,
            request.BucketId,
            request.DisplayName,
            request.Description,
            request.QuotaBytes,
            request.MaxObjectSizeBytes,
            request.AllowedContentTypes,
            request.AccessPolicy.ToDomain(),
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolBucket> ArchiveBucket(
        ArchiveBucketRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveBucketAsync(
            request.TenantId,
            request.BucketId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolBucket> RestoreBucket(
        RestoreBucketRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreBucketAsync(
            request.TenantId,
            request.BucketId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListObjectsResponse> ListObjects(
        ListObjectsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListObjectsAsync(
            request.TenantId,
            request.BucketId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeDeleted,
            context.CancellationToken);
        var response = new ListObjectsResponse { NextPageToken = result.NextPageToken };
        response.Objects.AddRange(result.Items.Select(StorageProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolObject> GetObject(
        GetObjectRequest request,
        ServerCallContext context) =>
        (await managementService.GetObjectAsync(
            request.TenantId,
            request.BucketId,
            request.ObjectId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolObject> UpdateObjectMetadata(
        UpdateObjectMetadataRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateObjectMetadataAsync(
            request.TenantId,
            request.BucketId,
            request.ObjectId,
            request.FileName,
            request.CustomMetadata,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUploadSession> CreateUploadSession(
        CreateUploadSessionRequest request,
        ServerCallContext context) =>
        (await managementService.CreateUploadSessionAsync(
            request.TenantId,
            request.BucketId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ObjectKey,
            request.FileName,
            request.ContentType,
            request.SizeBytes,
            request.Sha256,
            request.CustomMetadata,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolObject> CompleteUpload(
        CompleteUploadRequest request,
        ServerCallContext context) =>
        (await managementService.CompleteUploadAsync(
            request.TenantId,
            request.BucketId,
            request.UploadSessionId,
            request.ExpectedObjectVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolTicket> CreateDownloadUrl(
        CreateDownloadUrlRequest request,
        ServerCallContext context) =>
        (await managementService.CreateDownloadUrlAsync(
            request.TenantId,
            request.BucketId,
            request.ObjectId,
            request.LifetimeSeconds,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolObject> CopyObject(
        CopyObjectRequest request,
        ServerCallContext context) =>
        (await managementService.CopyObjectAsync(
            request.TenantId,
            request.BucketId,
            request.ObjectId,
            request.TargetBucketId,
            request.ObjectKey,
            request.FileName,
            request.CustomMetadata,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolObject> DeleteObject(
        DeleteObjectRequest request,
        ServerCallContext context) =>
        (await managementService.DeleteObjectAsync(
            request.TenantId,
            request.BucketId,
            request.ObjectId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();
}
