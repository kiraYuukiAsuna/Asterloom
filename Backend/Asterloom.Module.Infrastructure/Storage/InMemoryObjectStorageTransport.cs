using System.Security.Cryptography;
using System.Text;
using Asterloom.Modules.Storage.Model;
using Asterloom.Modules.Storage.Transport;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Infrastructure.Storage;

internal sealed class InMemoryObjectStorageTransport(TimeProvider timeProvider)
    : IObjectStorageTransport
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MemoryObject> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, TransferGrant> _grants = [];

    public Task<StorageTransferTicket> CreateUploadTicketAsync(
        Guid transferId,
        StorageObjectDescriptor descriptor,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = CreateToken();
        lock (_gate)
        {
            _grants[transferId] = new TransferGrant(
                HashToken(token),
                descriptor,
                FileName: string.Empty,
                expiresAt,
                IsUpload: true,
                Consumed: false);
        }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["content-type"] = descriptor.ContentType,
        };
        return Task.FromResult(new StorageTransferTicket(
            $"/api/v1/storage/transfers/uploads/{transferId:D}?token={Uri.EscapeDataString(token)}",
            "PUT",
            headers,
            expiresAt));
    }

    public Task<StoredObjectInspection> InspectAsync(
        StorageObjectDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_objects.TryGetValue(descriptor.PhysicalKey, out var item))
            {
                return Task.FromResult(new StoredObjectInspection(false, 0, string.Empty, string.Empty));
            }
            return Task.FromResult(new StoredObjectInspection(
                true,
                item.Content.LongLength,
                item.Sha256,
                item.ContentType));
        }
    }

    public Task<StorageTransferTicket> CreateDownloadTicketAsync(
        Guid transferId,
        StorageObjectDescriptor descriptor,
        string fileName,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = CreateToken();
        lock (_gate)
        {
            if (!_objects.ContainsKey(descriptor.PhysicalKey))
            {
                throw new FileNotFoundException("The stored object does not exist.");
            }
            _grants[transferId] = new TransferGrant(
                HashToken(token),
                descriptor,
                fileName,
                expiresAt,
                IsUpload: false,
                Consumed: false);
        }
        return Task.FromResult(new StorageTransferTicket(
            $"/api/v1/storage/transfers/downloads/{transferId:D}?token={Uri.EscapeDataString(token)}",
            "GET",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            expiresAt));
    }

    public Task CopyAsync(
        string sourcePhysicalKey,
        string targetPhysicalKey,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_objects.TryGetValue(sourcePhysicalKey, out var source))
            {
                throw new FileNotFoundException("The source object does not exist.");
            }
            _objects[targetPhysicalKey] = source with
            {
                Content = source.Content.ToArray(),
                Metadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal),
            };
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(string physicalKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _objects.Remove(physicalKey);
            return Task.CompletedTask;
        }
    }

    public async Task<bool> TryAcceptLocalUploadAsync(
        Guid transferId,
        string token,
        Stream content,
        string? contentType,
        long? contentLength,
        CancellationToken cancellationToken)
    {
        TransferGrant grant;
        lock (_gate)
        {
            if (!_grants.TryGetValue(transferId, out grant!)
                || !grant.IsUpload
                || grant.Consumed
                || grant.ExpiresAt <= timeProvider.GetUtcNow()
                || !TokenMatches(grant.TokenHash, token)
                || !string.Equals(grant.Descriptor.ContentType, contentType, StringComparison.OrdinalIgnoreCase)
                || (contentLength.HasValue && contentLength.Value != grant.Descriptor.SizeBytes))
            {
                return false;
            }
        }

        if (grant.Descriptor.SizeBytes > int.MaxValue)
        {
            return false;
        }
        using var destination = new MemoryStream((int)grant.Descriptor.SizeBytes);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total = checked(total + read);
            if (total > grant.Descriptor.SizeBytes)
            {
                return false;
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total != grant.Descriptor.SizeBytes)
        {
            return false;
        }
        var bytes = destination.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        lock (_gate)
        {
            if (!_grants.TryGetValue(transferId, out var current)
                || current.Consumed
                || current.ExpiresAt <= timeProvider.GetUtcNow()
                || !TokenMatches(current.TokenHash, token))
            {
                return false;
            }
            _objects[current.Descriptor.PhysicalKey] = new MemoryObject(
                bytes,
                current.Descriptor.ContentType,
                digest,
                new Dictionary<string, string>(current.Descriptor.Metadata, StringComparer.Ordinal));
            _grants[transferId] = current with { Consumed = true };
            return true;
        }
    }

    public Task<LocalObjectDownload?> TryOpenLocalDownloadAsync(
        Guid transferId,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_grants.TryGetValue(transferId, out var grant)
                || grant.IsUpload
                || grant.ExpiresAt <= timeProvider.GetUtcNow()
                || !TokenMatches(grant.TokenHash, token)
                || !_objects.TryGetValue(grant.Descriptor.PhysicalKey, out var item))
            {
                return Task.FromResult<LocalObjectDownload?>(null);
            }
            return Task.FromResult<LocalObjectDownload?>(new LocalObjectDownload(
                new MemoryStream(item.Content, writable: false),
                item.ContentType,
                item.Content.LongLength,
                grant.FileName));
        }
    }

    private static string CreateToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static bool TokenMatches(byte[] expectedHash, string token) =>
        CryptographicOperations.FixedTimeEquals(expectedHash, HashToken(token));

    private sealed record TransferGrant(
        byte[] TokenHash,
        StorageObjectDescriptor Descriptor,
        string FileName,
        DateTimeOffset ExpiresAt,
        bool IsUpload,
        bool Consumed);

    private sealed record MemoryObject(
        byte[] Content,
        string ContentType,
        string Sha256,
        IReadOnlyDictionary<string, string> Metadata);
}
