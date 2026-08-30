using System.Net;
using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Asterloom.Modules.Storage.Model;
using Asterloom.Modules.Storage.Transport;
using Microsoft.Extensions.Configuration;

namespace Asterloom.Modules.Infrastructure.Storage;

internal sealed class S3ObjectStorageTransport : IObjectStorageTransport, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly AmazonS3Client _signingClient;
    private readonly string _bucketName;
    private readonly SemaphoreSlim _bucketGate = new(1, 1);
    private bool _bucketReady;

    public S3ObjectStorageTransport(IConfiguration configuration)
    {
        var endpoint = configuration["Storage:Endpoint"]?.Trim();
        var publicEndpoint = configuration["Storage:PublicEndpoint"]?.Trim();
        var region = configuration["Storage:Region"]?.Trim();
        var accessKey = configuration["Storage:AccessKey"]?.Trim();
        var secretKey = configuration["Storage:SecretKey"];
        var forcePathStyle = configuration.GetValue("Storage:ForcePathStyle", false);
        _bucketName = configuration["Storage:PhysicalBucket"]?.Trim()
            ?? "asterloom-objects";

        var clientConfig = CreateConfig(endpoint, region, forcePathStyle);
        var signingConfig = CreateConfig(
            string.IsNullOrWhiteSpace(publicEndpoint) ? endpoint : publicEndpoint,
            region,
            forcePathStyle);
        if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
        {
            var credentials = new BasicAWSCredentials(accessKey, secretKey);
            _client = new AmazonS3Client(credentials, clientConfig);
            _signingClient = new AmazonS3Client(credentials, signingConfig);
        }
        else
        {
            _client = new AmazonS3Client(clientConfig);
            _signingClient = new AmazonS3Client(signingConfig);
        }
    }

    public async Task<StorageTransferTicket> CreateUploadTicketAsync(
        Guid transferId,
        StorageObjectDescriptor descriptor,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = descriptor.PhysicalKey,
            Verb = HttpVerb.PUT,
            ContentType = descriptor.ContentType,
            Expires = expiresAt.UtcDateTime,
        };
        request.Metadata.Add("asterloom-sha256", descriptor.Sha256);
        request.Metadata.Add("asterloom-size", descriptor.SizeBytes.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        var url = await _signingClient.GetPreSignedURLAsync(request);
        return new StorageTransferTicket(
            url,
            "PUT",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["content-type"] = descriptor.ContentType,
                ["x-amz-meta-asterloom-sha256"] = descriptor.Sha256,
                ["x-amz-meta-asterloom-size"] = descriptor.SizeBytes.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            },
            expiresAt);
    }

    public async Task<StoredObjectInspection> InspectAsync(
        StorageObjectDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        try
        {
            using var response = await _client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = descriptor.PhysicalKey,
                },
                cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long size = 0;
            int read;
            while ((read = await response.ResponseStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                size = checked(size + read);
                hash.AppendData(buffer, 0, read);
            }
            return new StoredObjectInspection(
                true,
                size,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                response.Headers.ContentType ?? string.Empty);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return new StoredObjectInspection(false, 0, string.Empty, string.Empty);
        }
    }

    public async Task<StorageTransferTicket> CreateDownloadTicketAsync(
        Guid transferId,
        StorageObjectDescriptor descriptor,
        string fileName,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = descriptor.PhysicalKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentType = descriptor.ContentType,
                ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fileName)}",
            },
        };
        return new StorageTransferTicket(
            await _signingClient.GetPreSignedURLAsync(request),
            "GET",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            expiresAt);
    }

    public async Task CopyAsync(
        string sourcePhysicalKey,
        string targetPhysicalKey,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        await _client.CopyObjectAsync(
            _bucketName,
            sourcePhysicalKey,
            _bucketName,
            targetPhysicalKey,
            cancellationToken);
    }

    public async Task DeleteAsync(string physicalKey, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        await _client.DeleteObjectAsync(_bucketName, physicalKey, cancellationToken);
    }

    public Task<bool> TryAcceptLocalUploadAsync(
        Guid transferId,
        string token,
        Stream content,
        string? contentType,
        long? contentLength,
        CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<LocalObjectDownload?> TryOpenLocalDownloadAsync(
        Guid transferId,
        string token,
        CancellationToken cancellationToken) => Task.FromResult<LocalObjectDownload?>(null);

    public void Dispose()
    {
        _client.Dispose();
        if (!ReferenceEquals(_client, _signingClient))
        {
            _signingClient.Dispose();
        }
        _bucketGate.Dispose();
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucketReady)
        {
            return;
        }
        await _bucketGate.WaitAsync(cancellationToken);
        try
        {
            if (_bucketReady)
            {
                return;
            }
            if (!await AmazonS3Util.DoesS3BucketExistV2Async(_client, _bucketName))
            {
                await _client.PutBucketAsync(
                    new PutBucketRequest { BucketName = _bucketName },
                    cancellationToken);
            }
            _bucketReady = true;
        }
        finally
        {
            _bucketGate.Release();
        }
    }

    private static AmazonS3Config CreateConfig(
        string? endpoint,
        string? region,
        bool forcePathStyle)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = forcePathStyle,
        };
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            config.ServiceURL = endpoint;
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(
                string.IsNullOrWhiteSpace(region) ? "us-east-1" : region);
        }
        return config;
    }
}
