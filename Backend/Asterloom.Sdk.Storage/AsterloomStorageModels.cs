using System.Text.Json;

namespace Asterloom.Sdk.Storage;

public sealed record AsterloomStorageScope(Guid TenantId);

public sealed record AsterloomStorageTransferTicket(
    Uri Url,
    HttpMethod Method,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);

public sealed record AsterloomStorageObject(
    Guid Id,
    Guid BucketId,
    Guid? ApplicationId,
    Guid? EnvironmentId,
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

public sealed record AsterloomStorageUploadSession(
    Guid Id,
    AsterloomStorageObject StorageObject,
    AsterloomStorageTransferTicket Transfer,
    string Status,
    DateTimeOffset ExpiresAt);

public sealed record AsterloomStorageUploadRequest(
    Guid BucketId,
    string ObjectKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    Guid? ApplicationId = null,
    Guid? EnvironmentId = null,
    IReadOnlyDictionary<string, string>? CustomMetadata = null);

public sealed class AsterloomStorageClientOptions
{
    public required AsterloomStorageScope Scope { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public bool AllowInsecureTransferUrls { get; init; }

    public JsonSerializerOptions SerializerOptions { get; init; } = new(JsonSerializerDefaults.Web);

    internal void Validate(HttpClient apiClient)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(Scope);
        ArgumentNullException.ThrowIfNull(SerializerOptions);
        if (apiClient.BaseAddress is null)
        {
            throw new ArgumentException("HttpClient.BaseAddress must identify the Asterloom server.");
        }
        if (Scope.TenantId == Guid.Empty)
        {
            throw new ArgumentException("Storage tenant identifier cannot be empty.");
        }
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentException("Request timeout must be between zero and 30 minutes.");
        }
    }
}
