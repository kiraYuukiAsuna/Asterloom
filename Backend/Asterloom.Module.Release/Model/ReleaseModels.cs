using Asterloom.Modules.Storage.Model;
using Asterloom.Targeting;

namespace Asterloom.Modules.Release.Model;

public enum ReleaseSigningKeyStatus : short
{
    Active = 1,
    Archived = 2,
}

public enum ReleaseArtifactStatus : short
{
    Uploading = 1,
    Verified = 2,
    Rejected = 3,
    Archived = 4,
}

public enum ReleaseArtifactKind : short
{
    Full = 1,
    Delta = 2,
}

public enum ReleaseChannelStatus : short
{
    Active = 1,
    Archived = 2,
}

public enum DesktopReleaseStatus : short
{
    Draft = 1,
    Published = 2,
    Paused = 3,
    RolledBack = 4,
}

public enum ReleaseValidationSeverity
{
    Error = 1,
    Warning = 2,
}

public enum UpdateDecisionReason
{
    UpdateAvailable = 1,
    Current = 2,
    ChannelEmpty = 3,
    ReleasePaused = 4,
    TargetingMiss = 5,
    RolloutExcluded = 6,
    NoCompatibleArtifact = 7,
}

public sealed record ReleaseSigningKey(
    Guid Id,
    Guid TenantId,
    string Key,
    string DisplayName,
    string Algorithm,
    string Fingerprint,
    string PublicKeyPem,
    ReleaseSigningKeyStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record ReleaseChannel(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    string Key,
    string DisplayName,
    string Description,
    ReleaseChannelStatus Status,
    Guid? ActiveReleaseId,
    Guid? PreviousReleaseId,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record ReleaseArtifact(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    string ReleaseVersion,
    string TargetRuntimeId,
    ReleaseArtifactKind ArtifactKind,
    string DeltaFromVersion,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    Guid SigningKeyId,
    string Signature,
    ReleaseArtifactStatus Status,
    string FailureReason,
    Guid StorageBucketId,
    Guid StorageObjectId,
    Guid UploadSessionId,
    long StorageObjectVersion,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? ArchivedAt);

public sealed record ArtifactUploadDetails(
    ReleaseArtifact Artifact,
    StorageUploadSessionDetails UploadSession);

public sealed record DesktopRelease(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    Guid ChannelId,
    string ReleaseVersion,
    string DisplayName,
    string ReleaseNotes,
    IReadOnlyList<Guid> ArtifactIds,
    uint RolloutBasisPoints,
    Guid? TargetSegmentId,
    bool Mandatory,
    string MinimumVersion,
    string BucketingSalt,
    DesktopReleaseStatus Status,
    long Revision,
    string ManifestPayloadJson,
    string ManifestSha256,
    string ManifestSignature,
    Guid? ManifestSigningKeyId,
    string ManifestSigningKeyFingerprint,
    DateTimeOffset? ManifestGeneratedAt,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? PausedAt,
    DateTimeOffset? RolledBackAt);

public sealed record ReleaseManifestArtifact(
    Guid ArtifactId,
    string TargetRuntimeId,
    ReleaseArtifactKind ArtifactKind,
    string DeltaFromVersion,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string Signature,
    Guid SigningKeyId,
    string SigningKeyFingerprint);

public sealed record ReleaseManifest(
    Guid ReleaseId,
    string ChannelKey,
    string ReleaseVersion,
    string DisplayName,
    string ReleaseNotes,
    bool Mandatory,
    string MinimumVersion,
    long Revision,
    IReadOnlyList<ReleaseManifestArtifact> Artifacts,
    string PayloadJson,
    string Sha256,
    string Signature,
    Guid? SigningKeyId,
    string SigningKeyFingerprint,
    DateTimeOffset GeneratedAt);

public sealed record ReleaseValidationIssue(
    ReleaseValidationSeverity Severity,
    string Code,
    string Path,
    string Message);

public sealed record ReleaseValidationResult(
    bool Valid,
    IReadOnlyList<ReleaseValidationIssue> Issues,
    ReleaseManifest CandidateManifest);

public sealed record ReleaseScope(
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId);

public sealed record ReleasePageRequest(
    int Offset,
    int PageSize,
    string Query,
    bool IncludeInactive);

public sealed record ReleaseStorePage<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed record ReleaseListResult<T>(IReadOnlyList<T> Items, string NextPageToken);

public sealed record UpdateCheckRequest(
    ReleaseScope Scope,
    string ChannelKey,
    string CurrentVersion,
    string TargetRuntimeId,
    TargetingEvaluationContext Context);

public sealed record ReleaseArtifactDownload(
    ReleaseArtifact Artifact,
    StorageTransferTicket Download);

public sealed record UpdateDecision(
    bool UpdateAvailable,
    UpdateDecisionReason Reason,
    ReleaseChannel? Channel,
    DesktopRelease? Release,
    ReleaseManifest? Manifest,
    ReleaseArtifact? SelectedArtifact,
    StorageTransferTicket? Download,
    IReadOnlyList<ReleaseArtifactDownload> ArtifactDownloads,
    bool Mandatory,
    bool BucketEvaluated,
    uint Bucket,
    uint RolloutBasisPoints,
    IReadOnlyList<string> Trace);
