using Asterloom.Modules.Storage.Model;

namespace Asterloom.Modules.Storage.Transport;

public interface IObjectStorageTransport
{
    Task<StorageTransferTicket> CreateUploadTicketAsync(
        Guid transferId,
        StorageObjectDescriptor descriptor,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task<StoredObjectInspection> InspectAsync(
        StorageObjectDescriptor descriptor,
        CancellationToken cancellationToken);

    Task<bool> TryReadAsync(
        StorageObjectDescriptor descriptor,
        Func<Stream, CancellationToken, Task> reader,
        CancellationToken cancellationToken);

    Task<StorageTransferTicket> CreateDownloadTicketAsync(
        Guid transferId,
        StorageObjectDescriptor descriptor,
        string fileName,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task CopyAsync(
        string sourcePhysicalKey,
        string targetPhysicalKey,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken);

    Task DeleteAsync(string physicalKey, CancellationToken cancellationToken);

    Task<bool> TryAcceptLocalUploadAsync(
        Guid transferId,
        string token,
        Stream content,
        string? contentType,
        long? contentLength,
        CancellationToken cancellationToken);

    Task<LocalObjectDownload?> TryOpenLocalDownloadAsync(
        Guid transferId,
        string token,
        CancellationToken cancellationToken);
}
