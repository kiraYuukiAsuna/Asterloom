using System.Text.Json;
using Asterloom.Targeting;

namespace Asterloom.Sdk.Release;

public enum AsterloomReleaseArtifactKind
{
    Full = 1,
    Delta = 2,
}

public enum AsterloomUpdateDecisionReason
{
    UpdateAvailable = 1,
    Current = 2,
    ChannelEmpty = 3,
    ReleasePaused = 4,
    TargetingMiss = 5,
    RolloutExcluded = 6,
    NoCompatibleArtifact = 7,
}

public sealed record AsterloomReleaseScope(
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId);

public sealed record AsterloomReleaseManifestArtifact(
    Guid ArtifactId,
    string TargetRuntimeId,
    AsterloomReleaseArtifactKind ArtifactKind,
    string? DeltaFromVersion,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string Signature,
    Guid SigningKeyId,
    string SigningKeyFingerprint);

public sealed record AsterloomReleaseManifest(
    Guid ReleaseId,
    string ChannelKey,
    string ReleaseVersion,
    string DisplayName,
    string ReleaseNotes,
    bool Mandatory,
    string MinimumVersion,
    long Revision,
    IReadOnlyList<AsterloomReleaseManifestArtifact> Artifacts,
    string PayloadJson,
    string Sha256,
    string Signature,
    Guid SigningKeyId,
    string SigningKeyFingerprint,
    DateTimeOffset GeneratedAt);

public sealed record AsterloomReleaseArtifact(
    Guid Id,
    string ReleaseVersion,
    string TargetRuntimeId,
    AsterloomReleaseArtifactKind ArtifactKind,
    string? DeltaFromVersion,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string Signature,
    Guid SigningKeyId);

public sealed record AsterloomReleaseTransferTicket(
    Uri Url,
    HttpMethod Method,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);

public sealed record AsterloomUpdateDecision(
    bool UpdateAvailable,
    AsterloomUpdateDecisionReason Reason,
    AsterloomReleaseManifest? Manifest,
    AsterloomReleaseArtifact? SelectedArtifact,
    AsterloomReleaseTransferTicket? Download,
    bool Mandatory,
    bool BucketEvaluated,
    uint Bucket,
    uint RolloutBasisPoints,
    IReadOnlyList<string> Trace);

public sealed class AsterloomReleaseClientOptions
{
    public required AsterloomReleaseScope Scope { get; init; }

    public required string TargetRuntimeId { get; init; }

    public required IReadOnlyDictionary<string, string> TrustedPublicKeysByFingerprint { get; init; }

    public string PackageId { get; init; } = "asterloom-app";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public bool AllowInsecureDownloadUrls { get; init; }

    public JsonSerializerOptions SerializerOptions { get; init; } = new(JsonSerializerDefaults.Web);

    internal void Validate(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(Scope);
        ArgumentNullException.ThrowIfNull(TrustedPublicKeysByFingerprint);
        ArgumentNullException.ThrowIfNull(SerializerOptions);
        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException("HttpClient.BaseAddress must identify the Asterloom server.");
        }
        if (Scope.TenantId == Guid.Empty
            || Scope.ApplicationId == Guid.Empty
            || Scope.EnvironmentId == Guid.Empty)
        {
            throw new ArgumentException("Release scope identifiers cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(TargetRuntimeId) || TargetRuntimeId.Length > 100)
        {
            throw new ArgumentException("A target runtime identifier is required.");
        }
        if (string.IsNullOrWhiteSpace(PackageId) || PackageId.Length > 200)
        {
            throw new ArgumentException("A Velopack package identifier is required.");
        }
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentException("Request timeout must be between zero and 30 minutes.");
        }
        AsterloomReleaseVerifier.ValidateTrustStore(TrustedPublicKeysByFingerprint);
    }
}

public static class AsterloomReleaseContext
{
    public static TargetingEvaluationContext Create(
        AsterloomReleaseScope scope,
        string targetingKey,
        string? userId = null,
        string? clientVersion = null,
        string? platform = null,
        string? region = null,
        string? language = null,
        IReadOnlyDictionary<string, TargetingValue>? attributes = null) =>
        new(
            targetingKey,
            scope.ApplicationId,
            scope.EnvironmentId,
            userId,
            clientVersion,
            platform,
            region,
            language,
            attributes);
}

public sealed class AsterloomReleaseIntegrityException(string message)
    : IOException(message);
