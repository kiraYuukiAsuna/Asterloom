using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Asterloom.Sdk.Storage;

public sealed class AsterloomStorageClient : IDisposable
{
    private readonly HttpClient _apiClient;
    private readonly HttpClient _transferClient;
    private readonly AsterloomStorageClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly bool _ownsTransferClient;

    public AsterloomStorageClient(
        HttpClient apiClient,
        AsterloomStorageClientOptions options,
        HttpClient? transferClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(apiClient);
        _apiClient = apiClient;
        _options = options;
        _jsonOptions = new JsonSerializerOptions(options.SerializerOptions)
        {
            NumberHandling = options.SerializerOptions.NumberHandling
                | JsonNumberHandling.AllowReadingFromString,
        };
        _transferClient = transferClient ?? new HttpClient { BaseAddress = apiClient.BaseAddress };
        _ownsTransferClient = transferClient is null;
    }

    public async Task<AsterloomStorageUploadSession> CreateUploadSessionAsync(
        AsterloomStorageUploadRequest upload,
        CancellationToken cancellationToken = default)
    {
        ValidateUpload(upload);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BucketPath(upload.BucketId)}/uploads")
        {
            Content = JsonContent.Create(new
            {
                applicationId = upload.ApplicationId?.ToString("D") ?? string.Empty,
                environmentId = upload.EnvironmentId?.ToString("D") ?? string.Empty,
                upload.ObjectKey,
                upload.FileName,
                upload.ContentType,
                upload.SizeBytes,
                upload.Sha256,
                customMetadata = upload.CustomMetadata
                    ?? new Dictionary<string, string>(StringComparer.Ordinal),
            }, options: _jsonOptions),
        };
        using var response = await SendApiAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<UploadSessionDto>(
            _jsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The storage upload session response is empty.");
        return ToModel(dto);
    }

    public async Task UploadContentAsync(
        AsterloomStorageUploadSession session,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(content);
        var uri = ResolveTransferUri(session.Transfer.Url);
        using var request = new HttpRequestMessage(session.Transfer.Method, uri)
        {
            Content = new StreamContent(content),
        };
        foreach (var header in session.Transfer.RequiredHeaders)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        using var response = await _transferClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AsterloomStorageObject> CompleteUploadAsync(
        AsterloomStorageUploadSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BucketPath(session.StorageObject.BucketId)}/uploads/{session.Id:D}:complete")
        {
            Content = JsonContent.Create(
                new { expectedObjectVersion = session.StorageObject.Version },
                options: _jsonOptions),
        };
        using var response = await SendApiAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<ObjectDto>(
            _jsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The storage completion response is empty.");
        return ToModel(dto);
    }

    public async Task<AsterloomStorageObject> UploadAsync(
        AsterloomStorageUploadRequest upload,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var session = await CreateUploadSessionAsync(upload, cancellationToken)
            .ConfigureAwait(false);
        await UploadContentAsync(session, content, cancellationToken).ConfigureAwait(false);
        return await CompleteUploadAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadToAsync(
        AsterloomStorageObject storageObject,
        Stream destination,
        TimeSpan? ticketLifetime = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storageObject);
        ArgumentNullException.ThrowIfNull(destination);
        var lifetime = ticketLifetime ?? TimeSpan.FromMinutes(5);
        if (lifetime < TimeSpan.FromSeconds(30) || lifetime > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticketLifetime),
                "Ticket lifetime must be between 30 seconds and 15 minutes.");
        }
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BucketPath(storageObject.BucketId)}/objects/{storageObject.Id:D}:download")
        {
            Content = JsonContent.Create(
                new { lifetimeSeconds = (int)lifetime.TotalSeconds },
                options: _jsonOptions),
        };
        using var response = await SendApiAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var ticketDto = await response.Content.ReadFromJsonAsync<TransferDto>(
            _jsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The storage download response is empty.");
        var ticket = ToModel(ticketDto);
        using var downloadRequest = new HttpRequestMessage(ticket.Method, ResolveTransferUri(ticket.Url));
        foreach (var header in ticket.RequiredHeaders)
        {
            downloadRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        using var downloadResponse = await _transferClient.SendAsync(
            downloadRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        downloadResponse.EnsureSuccessStatusCode();
        await using var source = await downloadResponse.Content.ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long size = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            size = checked(size + read);
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
        }
        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (size != storageObject.SizeBytes
            || !string.Equals(actualHash, storageObject.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Downloaded object failed its size or SHA-256 integrity check.");
        }
    }

    public void Dispose()
    {
        if (_ownsTransferClient)
        {
            _transferClient.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendApiAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        return await _apiClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
    }

    private Uri ResolveTransferUri(Uri transferUri)
    {
        var resolved = transferUri.IsAbsoluteUri
            ? transferUri
            : new Uri(_apiClient.BaseAddress!, transferUri);
        if (!_options.AllowInsecureTransferUrls
            && !string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Storage transfer URLs must use HTTPS unless insecure development transfers are explicitly enabled.");
        }
        if (!string.Equals(resolved.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage transfer URL must use HTTP or HTTPS.");
        }
        return resolved;
    }

    private string BucketPath(Guid bucketId) =>
        $"api/v1/tenants/{_options.Scope.TenantId:D}/storage/buckets/{bucketId:D}";

    private static void ValidateUpload(AsterloomStorageUploadRequest upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (upload.BucketId == Guid.Empty)
        {
            throw new ArgumentException("Bucket identifier cannot be empty.");
        }
        if (upload.SizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(upload), "Object size must be positive.");
        }
        if (upload.Sha256.Length != 64 || upload.Sha256.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 must be a 64-character hexadecimal digest.");
        }
    }

    private static AsterloomStorageUploadSession ToModel(UploadSessionDto dto) => new(
        Guid.Parse(dto.Id),
        ToModel(dto.StorageObject),
        ToModel(dto.Transfer),
        dto.Status,
        dto.ExpiresAt);

    private static AsterloomStorageObject ToModel(ObjectDto dto) => new(
        Guid.Parse(dto.Id),
        Guid.Parse(dto.BucketId),
        ParseOptional(dto.ApplicationId),
        ParseOptional(dto.EnvironmentId),
        dto.ObjectKey,
        dto.FileName,
        dto.ContentType,
        dto.SizeBytes,
        dto.Sha256,
        dto.CustomMetadata,
        dto.Status,
        dto.Version,
        dto.CreatedAt,
        dto.CompletedAt);

    private static AsterloomStorageTransferTicket ToModel(TransferDto dto) => new(
        new Uri(dto.Url, UriKind.RelativeOrAbsolute),
        new HttpMethod(dto.Method),
        dto.RequiredHeaders,
        dto.ExpiresAt);

    private static Guid? ParseOptional(string value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private sealed record UploadSessionDto(
        string Id,
        [property: JsonPropertyName("object")] ObjectDto StorageObject,
        TransferDto Transfer,
        string Status,
        DateTimeOffset ExpiresAt);

    private sealed record TransferDto(
        string Url,
        string Method,
        IReadOnlyDictionary<string, string> RequiredHeaders,
        DateTimeOffset ExpiresAt);

    private sealed record ObjectDto(
        string Id,
        string BucketId,
        string ApplicationId,
        string EnvironmentId,
        string ObjectKey,
        string FileName,
        string ContentType,
        long SizeBytes,
        string Sha256,
        IReadOnlyDictionary<string, string> CustomMetadata,
        string Status,
        long Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt);
}
