using Asterloom.Modules.Errors;
using Asterloom.Modules.Release.Model;
using Asterloom.Modules.Storage;
using Google.Protobuf.WellKnownTypes;
using ProtocolArtifact = Asterloom.Protocol.Release.V1.ReleaseArtifact;
using ProtocolArtifactKind = Asterloom.Protocol.Release.V1.ReleaseArtifactKind;
using ProtocolArtifactStatus = Asterloom.Protocol.Release.V1.ReleaseArtifactStatus;
using ProtocolArtifactUpload = Asterloom.Protocol.Release.V1.ArtifactUpload;
using ProtocolChannel = Asterloom.Protocol.Release.V1.ReleaseChannel;
using ProtocolChannelStatus = Asterloom.Protocol.Release.V1.ReleaseChannelStatus;
using ProtocolDecision = Asterloom.Protocol.Release.V1.UpdateDecision;
using ProtocolDecisionReason = Asterloom.Protocol.Release.V1.UpdateDecisionReason;
using ProtocolManifest = Asterloom.Protocol.Release.V1.ReleaseManifest;
using ProtocolManifestArtifact = Asterloom.Protocol.Release.V1.ReleaseManifestArtifact;
using ProtocolRelease = Asterloom.Protocol.Release.V1.DesktopRelease;
using ProtocolReleaseStatus = Asterloom.Protocol.Release.V1.DesktopReleaseStatus;
using ProtocolSigningKey = Asterloom.Protocol.Release.V1.ReleaseSigningKey;
using ProtocolSigningKeyStatus = Asterloom.Protocol.Release.V1.ReleaseSigningKeyStatus;
using ProtocolValidation = Asterloom.Protocol.Release.V1.ReleaseValidationResult;
using ProtocolValidationIssue = Asterloom.Protocol.Release.V1.ReleaseValidationIssue;
using ProtocolValidationSeverity = Asterloom.Protocol.Release.V1.ReleaseValidationSeverity;

namespace Asterloom.Modules.Release;

public static class ReleaseProtocolMapper
{
    public static ProtocolSigningKey ToProtocol(this ReleaseSigningKey signingKey) => new()
    {
        Id = signingKey.Id.ToString("D"),
        TenantId = signingKey.TenantId.ToString("D"),
        Key = signingKey.Key,
        DisplayName = signingKey.DisplayName,
        Algorithm = signingKey.Algorithm,
        Fingerprint = signingKey.Fingerprint,
        PublicKeyPem = signingKey.PublicKeyPem,
        Status = signingKey.Status switch
        {
            ReleaseSigningKeyStatus.Active => ProtocolSigningKeyStatus.Active,
            ReleaseSigningKeyStatus.Archived => ProtocolSigningKeyStatus.Archived,
            _ => ProtocolSigningKeyStatus.Unspecified,
        },
        Version = signingKey.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(signingKey.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(signingKey.UpdatedAt),
        ArchivedAt = signingKey.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolChannel ToProtocol(this ReleaseChannel channel) => new()
    {
        Id = channel.Id.ToString("D"),
        TenantId = channel.TenantId.ToString("D"),
        ApplicationId = channel.ApplicationId.ToString("D"),
        EnvironmentId = channel.EnvironmentId.ToString("D"),
        Key = channel.Key,
        DisplayName = channel.DisplayName,
        Description = channel.Description,
        Status = channel.Status switch
        {
            ReleaseChannelStatus.Active => ProtocolChannelStatus.Active,
            ReleaseChannelStatus.Archived => ProtocolChannelStatus.Archived,
            _ => ProtocolChannelStatus.Unspecified,
        },
        ActiveReleaseId = channel.ActiveReleaseId?.ToString("D") ?? string.Empty,
        PreviousReleaseId = channel.PreviousReleaseId?.ToString("D") ?? string.Empty,
        Version = channel.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(channel.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(channel.UpdatedAt),
        ArchivedAt = channel.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolArtifact ToProtocol(this ReleaseArtifact artifact) => new()
    {
        Id = artifact.Id.ToString("D"),
        TenantId = artifact.TenantId.ToString("D"),
        ApplicationId = artifact.ApplicationId.ToString("D"),
        EnvironmentId = artifact.EnvironmentId.ToString("D"),
        ReleaseVersion = artifact.ReleaseVersion,
        TargetRuntimeId = artifact.TargetRuntimeId,
        ArtifactKind = artifact.ArtifactKind switch
        {
            ReleaseArtifactKind.Full => ProtocolArtifactKind.Full,
            ReleaseArtifactKind.Delta => ProtocolArtifactKind.Delta,
            _ => ProtocolArtifactKind.Unspecified,
        },
        DeltaFromVersion = artifact.DeltaFromVersion,
        FileName = artifact.FileName,
        ContentType = artifact.ContentType,
        SizeBytes = artifact.SizeBytes,
        Sha256 = artifact.Sha256,
        SigningKeyId = artifact.SigningKeyId.ToString("D"),
        Signature = artifact.Signature,
        Status = artifact.Status switch
        {
            ReleaseArtifactStatus.Uploading => ProtocolArtifactStatus.Uploading,
            ReleaseArtifactStatus.Verified => ProtocolArtifactStatus.Verified,
            ReleaseArtifactStatus.Rejected => ProtocolArtifactStatus.Rejected,
            ReleaseArtifactStatus.Archived => ProtocolArtifactStatus.Archived,
            _ => ProtocolArtifactStatus.Unspecified,
        },
        FailureReason = artifact.FailureReason,
        StorageBucketId = artifact.StorageBucketId.ToString("D"),
        StorageObjectId = artifact.StorageObjectId.ToString("D"),
        UploadSessionId = artifact.UploadSessionId.ToString("D"),
        StorageObjectVersion = artifact.StorageObjectVersion,
        Version = artifact.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(artifact.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(artifact.UpdatedAt),
        VerifiedAt = artifact.VerifiedAt is { } verifiedAt
            ? Timestamp.FromDateTimeOffset(verifiedAt)
            : null,
        ArchivedAt = artifact.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolArtifactUpload ToProtocol(this ArtifactUploadDetails upload) => new()
    {
        Artifact = upload.Artifact.ToProtocol(),
        UploadSession = upload.UploadSession.ToProtocol(),
    };

    public static ProtocolRelease ToProtocol(this DesktopRelease release)
    {
        var result = new ProtocolRelease
        {
            Id = release.Id.ToString("D"),
            TenantId = release.TenantId.ToString("D"),
            ApplicationId = release.ApplicationId.ToString("D"),
            EnvironmentId = release.EnvironmentId.ToString("D"),
            ChannelId = release.ChannelId.ToString("D"),
            ReleaseVersion = release.ReleaseVersion,
            DisplayName = release.DisplayName,
            ReleaseNotes = release.ReleaseNotes,
            RolloutBasisPoints = release.RolloutBasisPoints,
            TargetSegmentId = release.TargetSegmentId?.ToString("D") ?? string.Empty,
            Mandatory = release.Mandatory,
            MinimumVersion = release.MinimumVersion,
            BucketingSalt = release.BucketingSalt,
            Status = release.Status switch
            {
                DesktopReleaseStatus.Draft => ProtocolReleaseStatus.Draft,
                DesktopReleaseStatus.Published => ProtocolReleaseStatus.Published,
                DesktopReleaseStatus.Paused => ProtocolReleaseStatus.Paused,
                DesktopReleaseStatus.RolledBack => ProtocolReleaseStatus.RolledBack,
                _ => ProtocolReleaseStatus.Unspecified,
            },
            Revision = release.Revision,
            ManifestPayloadJson = release.ManifestPayloadJson,
            ManifestSha256 = release.ManifestSha256,
            ManifestSignature = release.ManifestSignature,
            ManifestSigningKeyId = release.ManifestSigningKeyId?.ToString("D") ?? string.Empty,
            ManifestSigningKeyFingerprint = release.ManifestSigningKeyFingerprint,
            Version = release.Version,
            CreatedAt = Timestamp.FromDateTimeOffset(release.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(release.UpdatedAt),
            PublishedAt = release.PublishedAt is { } publishedAt
                ? Timestamp.FromDateTimeOffset(publishedAt)
                : null,
            PausedAt = release.PausedAt is { } pausedAt
                ? Timestamp.FromDateTimeOffset(pausedAt)
                : null,
            RolledBackAt = release.RolledBackAt is { } rolledBackAt
                ? Timestamp.FromDateTimeOffset(rolledBackAt)
                : null,
            ManifestGeneratedAt = release.ManifestGeneratedAt is { } generatedAt
                ? Timestamp.FromDateTimeOffset(generatedAt)
                : null,
        };
        result.ArtifactIds.AddRange(release.ArtifactIds.Select(static id => id.ToString("D")));
        return result;
    }

    public static ProtocolManifest ToProtocol(this ReleaseManifest manifest)
    {
        var result = new ProtocolManifest
        {
            ReleaseId = manifest.ReleaseId.ToString("D"),
            ChannelKey = manifest.ChannelKey,
            ReleaseVersion = manifest.ReleaseVersion,
            DisplayName = manifest.DisplayName,
            ReleaseNotes = manifest.ReleaseNotes,
            Mandatory = manifest.Mandatory,
            MinimumVersion = manifest.MinimumVersion,
            Revision = manifest.Revision,
            PayloadJson = manifest.PayloadJson,
            Sha256 = manifest.Sha256,
            Signature = manifest.Signature,
            SigningKeyId = manifest.SigningKeyId?.ToString("D") ?? string.Empty,
            SigningKeyFingerprint = manifest.SigningKeyFingerprint,
            GeneratedAt = Timestamp.FromDateTimeOffset(manifest.GeneratedAt),
        };
        result.Artifacts.AddRange(manifest.Artifacts.Select(ToProtocol));
        return result;
    }

    public static ProtocolValidation ToProtocol(this ReleaseValidationResult validation)
    {
        var result = new ProtocolValidation
        {
            Valid = validation.Valid,
            CandidateManifest = validation.CandidateManifest.ToProtocol(),
        };
        result.Issues.AddRange(validation.Issues.Select(ToProtocol));
        return result;
    }

    public static ProtocolDecision ToProtocol(this UpdateDecision decision)
    {
        var result = new ProtocolDecision
        {
            UpdateAvailable = decision.UpdateAvailable,
            Reason = decision.Reason switch
            {
                UpdateDecisionReason.UpdateAvailable => ProtocolDecisionReason.UpdateAvailable,
                UpdateDecisionReason.Current => ProtocolDecisionReason.Current,
                UpdateDecisionReason.ChannelEmpty => ProtocolDecisionReason.ChannelEmpty,
                UpdateDecisionReason.ReleasePaused => ProtocolDecisionReason.ReleasePaused,
                UpdateDecisionReason.TargetingMiss => ProtocolDecisionReason.TargetingMiss,
                UpdateDecisionReason.RolloutExcluded => ProtocolDecisionReason.RolloutExcluded,
                UpdateDecisionReason.NoCompatibleArtifact => ProtocolDecisionReason.NoCompatibleArtifact,
                _ => ProtocolDecisionReason.Unspecified,
            },
            Mandatory = decision.Mandatory,
            BucketEvaluated = decision.BucketEvaluated,
            Bucket = decision.Bucket,
            RolloutBasisPoints = decision.RolloutBasisPoints,
        };
        if (decision.Channel is not null)
        {
            result.Channel = decision.Channel.ToProtocol();
        }
        if (decision.Release is not null)
        {
            result.Release = decision.Release.ToProtocol();
        }
        if (decision.Manifest is not null)
        {
            result.Manifest = decision.Manifest.ToProtocol();
        }
        if (decision.SelectedArtifact is not null)
        {
            result.SelectedArtifact = decision.SelectedArtifact.ToProtocol();
        }
        if (decision.Download is not null)
        {
            result.Download = decision.Download.ToProtocol();
        }
        result.Trace.AddRange(decision.Trace);
        return result;
    }

    public static ReleaseArtifactKind ToDomain(this ProtocolArtifactKind artifactKind) =>
        artifactKind switch
        {
            ProtocolArtifactKind.Full => ReleaseArtifactKind.Full,
            ProtocolArtifactKind.Delta => ReleaseArtifactKind.Delta,
            _ => (ReleaseArtifactKind)0,
        };

    public static ReleaseScope ToReleaseScope(
        string tenantId,
        string applicationId,
        string environmentId) =>
        new(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            ParseId(environmentId, "environmentId"));

    private static ProtocolManifestArtifact ToProtocol(ReleaseManifestArtifact artifact) => new()
    {
        ArtifactId = artifact.ArtifactId.ToString("D"),
        TargetRuntimeId = artifact.TargetRuntimeId,
        ArtifactKind = artifact.ArtifactKind switch
        {
            ReleaseArtifactKind.Full => ProtocolArtifactKind.Full,
            ReleaseArtifactKind.Delta => ProtocolArtifactKind.Delta,
            _ => ProtocolArtifactKind.Unspecified,
        },
        DeltaFromVersion = artifact.DeltaFromVersion,
        FileName = artifact.FileName,
        ContentType = artifact.ContentType,
        SizeBytes = artifact.SizeBytes,
        Sha256 = artifact.Sha256,
        Signature = artifact.Signature,
        SigningKeyId = artifact.SigningKeyId.ToString("D"),
        SigningKeyFingerprint = artifact.SigningKeyFingerprint,
    };

    private static ProtocolValidationIssue ToProtocol(ReleaseValidationIssue issue) => new()
    {
        Severity = issue.Severity switch
        {
            ReleaseValidationSeverity.Error => ProtocolValidationSeverity.Error,
            ReleaseValidationSeverity.Warning => ProtocolValidationSeverity.Warning,
            _ => ProtocolValidationSeverity.Unspecified,
        },
        Code = issue.Code,
        Path = issue.Path,
        Message = issue.Message,
    };

    private static Guid ParseId(string? value, string field)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new AsterloomException(
                AsterloomErrorKind.InvalidArgument,
                "validation_failed",
                "One or more fields are invalid.",
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [field] = ["A valid identifier is required."],
                });
        }
        return id;
    }
}
