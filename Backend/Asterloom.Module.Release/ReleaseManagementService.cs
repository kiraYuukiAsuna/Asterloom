using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Asterloom.Modules.Release.Model;
using Asterloom.Modules.Release.Persistence;
using Asterloom.Modules.Storage;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;
using Asterloom.Targeting;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Release;

public sealed partial class ReleaseManagementService(
    IReleaseStore store,
    IPlatformResourceStore platformStore,
    ITargetingStore targetingStore,
    StorageManagementService storage,
    TimeProvider timeProvider)
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const long ArtifactBucketQuotaBytes = 100L * 1024 * 1024 * 1024;
    private const long MaximumArtifactSizeBytes = 4L * 1024 * 1024 * 1024;

    private static readonly IReadOnlyList<string> ArtifactContentTypes = ["*/*"];

    public async Task<ReleaseListResult<ReleaseSigningKey>> ListSigningKeysAsync(
        string tenantId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: false, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var result = await store.ListSigningKeysAsync(tenant, page, cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<ReleaseSigningKey> CreateSigningKeyAsync(
        string tenantId,
        string key,
        string displayName,
        string publicKeyPem,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, requireActive: true, cancellationToken);

        string normalizedPem;
        string fingerprint;
        try
        {
            (normalizedPem, fingerprint) = ReleaseCryptography.NormalizePublicKey(publicKeyPem);
        }
        catch (CryptographicException exception)
        {
            throw Invalid("publicKeyPem", exception.Message);
        }

        var now = timeProvider.GetUtcNow();
        var signingKey = new ReleaseSigningKey(
            Guid.CreateVersion7(),
            tenant,
            NormalizeKey(key),
            NormalizeDisplayName(displayName),
            ReleaseCryptography.Algorithm,
            fingerprint,
            normalizedPem,
            ReleaseSigningKeyStatus.Active,
            Version: 1,
            now,
            now,
            ArchivedAt: null);
        if (!await store.TryCreateSigningKeyAsync(signingKey, cancellationToken))
        {
            throw AlreadyExists(
                "release_signing_key_exists",
                "A signing key with this key or fingerprint already exists.");
        }
        return signingKey;
    }

    public Task<ReleaseSigningKey> ArchiveSigningKeyAsync(
        string tenantId,
        string signingKeyId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeSigningKeyStatusAsync(
            tenantId,
            signingKeyId,
            expectedVersion,
            ReleaseSigningKeyStatus.Archived,
            cancellationToken);

    public Task<ReleaseSigningKey> RestoreSigningKeyAsync(
        string tenantId,
        string signingKeyId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeSigningKeyStatusAsync(
            tenantId,
            signingKeyId,
            expectedVersion,
            ReleaseSigningKeyStatus.Active,
            cancellationToken);

    public async Task<ReleaseListResult<ReleaseChannel>> ListChannelsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var result = await store.ListChannelsAsync(scope, page, cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<ReleaseChannel> GetChannelAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string channelId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        return await RequireChannelAsync(scope, ParseId(channelId, "channelId"), cancellationToken);
    }

    public async Task<ReleaseChannel> CreateChannelAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string key,
        string displayName,
        string? description,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var channel = new ReleaseChannel(
            Guid.CreateVersion7(),
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            NormalizeKey(key),
            NormalizeDisplayName(displayName),
            NormalizeDescription(description),
            ReleaseChannelStatus.Active,
            ActiveReleaseId: null,
            PreviousReleaseId: null,
            Version: 1,
            now,
            now,
            ArchivedAt: null);
        if (!await store.TryCreateChannelAsync(channel, cancellationToken))
        {
            throw AlreadyExists(
                "release_channel_key_exists",
                "A release channel with this key already exists in the environment.");
        }
        return channel;
    }

    public async Task<ReleaseChannel> UpdateChannelAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string channelId,
        string displayName,
        string? description,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireChannelAsync(
            scope,
            ParseId(channelId, "channelId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current);
        var updated = current with
        {
            DisplayName = NormalizeDisplayName(displayName),
            Description = NormalizeDescription(description),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        if (!await store.TryUpdateChannelAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }
        return updated;
    }

    public Task<ReleaseChannel> ArchiveChannelAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string channelId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeChannelStatusAsync(
            tenantId,
            applicationId,
            environmentId,
            channelId,
            expectedVersion,
            ReleaseChannelStatus.Archived,
            cancellationToken);

    public Task<ReleaseChannel> RestoreChannelAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string channelId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeChannelStatusAsync(
            tenantId,
            applicationId,
            environmentId,
            channelId,
            expectedVersion,
            ReleaseChannelStatus.Active,
            cancellationToken);

    public async Task<ReleaseListResult<ReleaseArtifact>> ListArtifactsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var result = await store.ListArtifactsAsync(scope, page, cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<ReleaseArtifact> GetArtifactAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string artifactId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        return await RequireArtifactAsync(scope, ParseId(artifactId, "artifactId"), cancellationToken);
    }

    public async Task<ArtifactUploadDetails> CreateArtifactUploadAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string releaseVersion,
        string targetRuntimeId,
        ReleaseArtifactKind artifactKind,
        string? deltaFromVersion,
        string fileName,
        string contentType,
        long sizeBytes,
        string sha256,
        string signingKeyId,
        string signature,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var normalizedVersion = NormalizeVersion(releaseVersion, "releaseVersion");
        var normalizedRuntime = NormalizeRuntimeId(targetRuntimeId);
        var normalizedDelta = NormalizeDeltaVersion(artifactKind, deltaFromVersion, normalizedVersion);
        var normalizedFileName = NormalizeFileName(fileName);
        var normalizedContentType = NormalizeContentType(contentType);
        var normalizedSha = NormalizeSha256(sha256);
        var normalizedSignature = NormalizeSignature(signature);
        if (sizeBytes is < 1 or > MaximumArtifactSizeBytes)
        {
            throw Invalid(
                "sizeBytes",
                $"Artifact size must be between 1 and {MaximumArtifactSizeBytes} bytes.");
        }

        var signingKey = await RequireSigningKeyAsync(
            scope.TenantId,
            ParseId(signingKeyId, "signingKeyId"),
            cancellationToken);
        RequireActive(signingKey);
        var existing = await store.GetArtifactByIdentityAsync(
            scope,
            normalizedVersion,
            normalizedRuntime,
            artifactKind,
            normalizedDelta,
            cancellationToken);
        if (existing is not null)
        {
            throw AlreadyExists(
                "release_artifact_exists",
                "An artifact with this version, runtime, kind, and delta source already exists.");
        }

        var bucket = await storage.EnsureSystemBucketAsync(
            scope.TenantId,
            "release-artifacts",
            "Release artifacts",
            ArtifactBucketQuotaBytes,
            MaximumArtifactSizeBytes,
            ArtifactContentTypes,
            cancellationToken);
        var artifactId = Guid.CreateVersion7();
        var upload = await storage.CreateUploadSessionAsync(
            scope.TenantId.ToString("D"),
            bucket.Id.ToString("D"),
            scope.ApplicationId.ToString("D"),
            scope.EnvironmentId.ToString("D"),
            $"artifacts/{artifactId:N}/{normalizedFileName}",
            normalizedFileName,
            normalizedContentType,
            sizeBytes,
            normalizedSha,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["asterloom.releaseVersion"] = normalizedVersion,
                ["asterloom.targetRuntimeId"] = normalizedRuntime,
                ["asterloom.artifactKind"] = artifactKind.ToString().ToLowerInvariant(),
            },
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var artifact = new ReleaseArtifact(
            artifactId,
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            normalizedVersion,
            normalizedRuntime,
            artifactKind,
            normalizedDelta,
            normalizedFileName,
            normalizedContentType,
            sizeBytes,
            normalizedSha,
            signingKey.Id,
            normalizedSignature,
            ReleaseArtifactStatus.Uploading,
            FailureReason: string.Empty,
            bucket.Id,
            upload.StorageObject.Id,
            upload.Session.Id,
            upload.StorageObject.Version,
            Version: 1,
            now,
            now,
            VerifiedAt: null,
            ArchivedAt: null);
        if (!await store.TryCreateArtifactAsync(artifact, cancellationToken))
        {
            throw AlreadyExists(
                "release_artifact_exists",
                "An artifact with this version, runtime, kind, and delta source already exists.");
        }
        return new(artifact, upload);
    }

    public async Task<ReleaseArtifact> CompleteArtifactUploadAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string artifactId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireArtifactAsync(
            scope,
            ParseId(artifactId, "artifactId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == ReleaseArtifactStatus.Verified)
        {
            return current;
        }
        if (current.Status != ReleaseArtifactStatus.Uploading)
        {
            throw FailedPrecondition(
                "release_artifact_not_uploading",
                "Only an uploading artifact can be completed.");
        }

        var storageObject = await storage.CompleteUploadAsync(
            scope.TenantId.ToString("D"),
            current.StorageBucketId.ToString("D"),
            current.UploadSessionId.ToString("D"),
            current.StorageObjectVersion,
            cancellationToken);
        var signingKey = await RequireSigningKeyAsync(
            scope.TenantId,
            current.SigningKeyId,
            cancellationToken);
        var signatureValid = signingKey.Status == ReleaseSigningKeyStatus.Active
            && ReleaseCryptography.VerifyDigestSignature(
                signingKey.PublicKeyPem,
                storageObject.Sha256,
                current.Signature);
        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = signatureValid
                ? ReleaseArtifactStatus.Verified
                : ReleaseArtifactStatus.Rejected,
            FailureReason = signatureValid
                ? string.Empty
                : "artifact_signature_invalid",
            StorageObjectVersion = storageObject.Version,
            Version = current.Version + 1,
            UpdatedAt = now,
            VerifiedAt = signatureValid ? now : null,
        };
        if (!await store.TryUpdateArtifactAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }
        return updated;
    }

    public async Task<ReleaseArtifact> ArchiveArtifactAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string artifactId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var current = await RequireArtifactAsync(
            scope,
            ParseId(artifactId, "artifactId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == ReleaseArtifactStatus.Archived)
        {
            return current;
        }
        if (current.Status == ReleaseArtifactStatus.Uploading)
        {
            throw FailedPrecondition(
                "release_artifact_upload_pending",
                "Complete the artifact upload before archiving it.");
        }
        if (await store.IsArtifactReferencedByLiveReleaseAsync(scope, current.Id, cancellationToken))
        {
            throw FailedPrecondition(
                "release_artifact_in_use",
                "The artifact is referenced by a draft, published, or paused release.");
        }
        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = ReleaseArtifactStatus.Archived,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = now,
        };
        if (!await store.TryUpdateArtifactAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }
        return updated;
    }

    public async Task<ReleaseListResult<DesktopRelease>> ListReleasesAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query, includeInactive);
        var result = await store.ListReleasesAsync(scope, page, cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<DesktopRelease> GetReleaseAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string releaseId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        return await RequireReleaseAsync(scope, ParseId(releaseId, "releaseId"), cancellationToken);
    }

    public async Task<DesktopRelease> CreateReleaseAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string channelId,
        string releaseVersion,
        string displayName,
        string? releaseNotes,
        IReadOnlyCollection<string> artifactIds,
        uint rolloutBasisPoints,
        string? targetSegmentId,
        bool mandatory,
        string? minimumVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var channel = await RequireChannelAsync(
            scope,
            ParseId(channelId, "channelId"),
            cancellationToken);
        RequireActive(channel);
        var normalizedVersion = NormalizeVersion(releaseVersion, "releaseVersion");
        var normalizedMinimum = NormalizeMinimumVersion(minimumVersion, normalizedVersion);
        var artifacts = ParseArtifactIds(artifactIds);
        var segmentId = ParseOptionalId(targetSegmentId, "targetSegmentId");
        RequireRollout(rolloutBasisPoints);
        if (await store.GetReleaseByVersionAsync(
                scope,
                channel.Id,
                normalizedVersion,
                cancellationToken) is not null)
        {
            throw AlreadyExists(
                "desktop_release_version_exists",
                "This channel already contains a release with the same version.");
        }

        var now = timeProvider.GetUtcNow();
        var release = new DesktopRelease(
            Guid.CreateVersion7(),
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            channel.Id,
            normalizedVersion,
            NormalizeDisplayName(displayName),
            NormalizeReleaseNotes(releaseNotes),
            artifacts,
            rolloutBasisPoints,
            segmentId,
            mandatory,
            normalizedMinimum,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            DesktopReleaseStatus.Draft,
            Revision: 1,
            ManifestPayloadJson: string.Empty,
            ManifestSha256: string.Empty,
            ManifestSignature: string.Empty,
            ManifestSigningKeyId: null,
            ManifestSigningKeyFingerprint: string.Empty,
            ManifestGeneratedAt: null,
            Version: 1,
            now,
            now,
            PublishedAt: null,
            PausedAt: null,
            RolledBackAt: null);
        if (!await store.TryCreateReleaseAsync(release, cancellationToken))
        {
            throw AlreadyExists(
                "desktop_release_version_exists",
                "This channel already contains a release with the same version.");
        }
        return release;
    }

    public async Task<DesktopRelease> UpdateReleaseDraftAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string releaseId,
        string displayName,
        string? releaseNotes,
        IReadOnlyCollection<string> artifactIds,
        uint rolloutBasisPoints,
        string? targetSegmentId,
        bool mandatory,
        string? minimumVersion,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireReleaseAsync(
            scope,
            ParseId(releaseId, "releaseId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireDraft(current);
        RequireRollout(rolloutBasisPoints);
        var updated = current with
        {
            DisplayName = NormalizeDisplayName(displayName),
            ReleaseNotes = NormalizeReleaseNotes(releaseNotes),
            ArtifactIds = ParseArtifactIds(artifactIds),
            RolloutBasisPoints = rolloutBasisPoints,
            TargetSegmentId = ParseOptionalId(targetSegmentId, "targetSegmentId"),
            Mandatory = mandatory,
            MinimumVersion = NormalizeMinimumVersion(minimumVersion, current.ReleaseVersion),
            Revision = current.Revision + 1,
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        if (!await store.TryUpdateReleaseAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }
        return updated;
    }

    public async Task<ReleaseValidationResult> ValidateReleaseAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string releaseId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var release = await RequireReleaseAsync(
            scope,
            ParseId(releaseId, "releaseId"),
            cancellationToken);
        return await ValidateReleaseCoreAsync(scope, release, cancellationToken);
    }

    public async Task<DesktopRelease> PublishReleaseAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string releaseId,
        string manifestSigningKeyId,
        string manifestSignature,
        long expectedVersion,
        long expectedChannelVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireReleaseAsync(
            scope,
            ParseId(releaseId, "releaseId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireDraft(current);
        var channel = await RequireChannelAsync(scope, current.ChannelId, cancellationToken);
        RequireVersion(channel.Version, expectedChannelVersion);
        RequireActive(channel);
        var validation = await ValidateReleaseCoreAsync(scope, current, cancellationToken);
        RequireValid(validation);
        var signingKey = await RequireSigningKeyAsync(
            scope.TenantId,
            ParseId(manifestSigningKeyId, "manifestSigningKeyId"),
            cancellationToken);
        RequireActive(signingKey);
        var signature = NormalizeSignature(manifestSignature);
        if (!ReleaseCryptography.VerifyDigestSignature(
                signingKey.PublicKeyPem,
                validation.CandidateManifest.Sha256,
                signature))
        {
            throw FailedPrecondition(
                "release_manifest_signature_invalid",
                "The detached manifest signature does not match the validated manifest hash.");
        }

        var now = timeProvider.GetUtcNow();
        var published = current with
        {
            Status = DesktopReleaseStatus.Published,
            ManifestPayloadJson = validation.CandidateManifest.PayloadJson,
            ManifestSha256 = validation.CandidateManifest.Sha256,
            ManifestSignature = signature,
            ManifestSigningKeyId = signingKey.Id,
            ManifestSigningKeyFingerprint = signingKey.Fingerprint,
            ManifestGeneratedAt = validation.CandidateManifest.GeneratedAt,
            Version = current.Version + 1,
            UpdatedAt = now,
            PublishedAt = now,
            PausedAt = null,
            RolledBackAt = null,
        };
        var activatedChannel = channel with
        {
            PreviousReleaseId = channel.ActiveReleaseId,
            ActiveReleaseId = published.Id,
            Version = channel.Version + 1,
            UpdatedAt = now,
        };
        if (!await store.TryPublishReleaseAsync(
                published,
                current.Version,
                activatedChannel,
                channel.Version,
                cancellationToken))
        {
            throw VersionConflict();
        }
        return published;
    }

    public async Task<DesktopRelease> PauseReleaseAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string releaseId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var current = await RequireReleaseAsync(
            scope,
            ParseId(releaseId, "releaseId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == DesktopReleaseStatus.Paused)
        {
            return current;
        }
        if (current.Status != DesktopReleaseStatus.Published)
        {
            throw FailedPrecondition(
                "desktop_release_not_published",
                "Only a published release can be paused.");
        }
        var channel = await RequireChannelAsync(scope, current.ChannelId, cancellationToken);
        if (channel.ActiveReleaseId != current.Id)
        {
            throw FailedPrecondition(
                "desktop_release_not_active",
                "Only the active release in a channel can be paused.");
        }
        var now = timeProvider.GetUtcNow();
        var paused = current with
        {
            Status = DesktopReleaseStatus.Paused,
            Version = current.Version + 1,
            UpdatedAt = now,
            PausedAt = now,
        };
        if (!await store.TryUpdateReleaseAsync(paused, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }
        return paused;
    }

    public async Task<DesktopRelease> PromoteReleaseAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string releaseId,
        uint rolloutBasisPoints,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireReleaseAsync(
            scope,
            ParseId(releaseId, "releaseId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireRollout(rolloutBasisPoints);
        if (current.Status is not (DesktopReleaseStatus.Published or DesktopReleaseStatus.Paused))
        {
            throw FailedPrecondition(
                "desktop_release_not_promotable",
                "Only a published or paused release can be promoted.");
        }
        if (rolloutBasisPoints < current.RolloutBasisPoints
            || (rolloutBasisPoints == current.RolloutBasisPoints
                && current.Status == DesktopReleaseStatus.Published))
        {
            throw Invalid(
                "rolloutBasisPoints",
                "Promotion must increase the rollout percentage; resuming a paused release may keep it unchanged.");
        }
        var channel = await RequireChannelAsync(scope, current.ChannelId, cancellationToken);
        RequireActive(channel);
        if (channel.ActiveReleaseId != current.Id)
        {
            throw FailedPrecondition(
                "desktop_release_not_active",
                "Only the active release in a channel can be promoted.");
        }
        var promoted = current with
        {
            RolloutBasisPoints = rolloutBasisPoints,
            Status = DesktopReleaseStatus.Published,
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
            PausedAt = null,
        };
        if (!await store.TryUpdateReleaseAsync(promoted, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }
        return promoted;
    }

    public async Task<DesktopRelease> RollbackReleaseAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string releaseId,
        string targetReleaseId,
        long expectedVersion,
        long expectedTargetVersion,
        long expectedChannelVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var currentId = ParseId(releaseId, "releaseId");
        var targetId = ParseId(targetReleaseId, "targetReleaseId");
        if (currentId == targetId)
        {
            throw Invalid("targetReleaseId", "The rollback target must be a different release.");
        }
        var current = await RequireReleaseAsync(scope, currentId, cancellationToken);
        var target = await RequireReleaseAsync(scope, targetId, cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireVersion(target.Version, expectedTargetVersion, "expectedTargetVersion");
        if (current.ChannelId != target.ChannelId)
        {
            throw Invalid("targetReleaseId", "The rollback target must belong to the same channel.");
        }
        if (current.Status is not (DesktopReleaseStatus.Published or DesktopReleaseStatus.Paused)
            || target.Status == DesktopReleaseStatus.Draft)
        {
            throw FailedPrecondition(
                "desktop_release_rollback_invalid",
                "Rollback requires an active published release and a previously published target.");
        }
        if (string.IsNullOrEmpty(target.ManifestSha256)
            || target.ManifestSigningKeyId is null)
        {
            throw FailedPrecondition(
                "desktop_release_target_unsigned",
                "The rollback target does not have a signed manifest.");
        }
        var channel = await RequireChannelAsync(scope, current.ChannelId, cancellationToken);
        RequireVersion(channel.Version, expectedChannelVersion, "expectedChannelVersion");
        RequireActive(channel);
        if (channel.ActiveReleaseId != current.Id)
        {
            throw FailedPrecondition(
                "desktop_release_not_active",
                "The release being rolled back is not the channel's active release.");
        }

        var now = timeProvider.GetUtcNow();
        var rolledBack = current with
        {
            Status = DesktopReleaseStatus.RolledBack,
            Version = current.Version + 1,
            UpdatedAt = now,
            RolledBackAt = now,
        };
        var restored = target with
        {
            Status = DesktopReleaseStatus.Published,
            Version = target.Version + 1,
            UpdatedAt = now,
            PausedAt = null,
            RolledBackAt = null,
        };
        var switched = channel with
        {
            PreviousReleaseId = current.Id,
            ActiveReleaseId = target.Id,
            Version = channel.Version + 1,
            UpdatedAt = now,
        };
        if (!await store.TryRollbackReleaseAsync(
                rolledBack,
                current.Version,
                restored,
                target.Version,
                switched,
                channel.Version,
                cancellationToken))
        {
            throw VersionConflict();
        }
        return restored;
    }

    public async Task<ReleaseManifest> GetReleaseManifestAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string releaseId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var release = await RequireReleaseAsync(
            scope,
            ParseId(releaseId, "releaseId"),
            cancellationToken);
        if (release.Status == DesktopReleaseStatus.Draft
            || release.ManifestGeneratedAt is null
            || release.ManifestSigningKeyId is null)
        {
            throw FailedPrecondition(
                "desktop_release_manifest_unpublished",
                "The release does not have a published, signed manifest.");
        }
        var manifest = await BuildManifestAsync(scope, release, cancellationToken);
        if (!string.Equals(manifest.Sha256, release.ManifestSha256, StringComparison.Ordinal)
            || !string.Equals(manifest.PayloadJson, release.ManifestPayloadJson, StringComparison.Ordinal))
        {
            throw FailedPrecondition(
                "desktop_release_manifest_integrity_failed",
                "The stored release no longer reconstructs to its signed manifest.");
        }
        return manifest with
        {
            Signature = release.ManifestSignature,
            SigningKeyId = release.ManifestSigningKeyId,
            SigningKeyFingerprint = release.ManifestSigningKeyFingerprint,
        };
    }

    private async Task<ReleaseValidationResult> ValidateReleaseCoreAsync(
        ReleaseScope scope,
        DesktopRelease release,
        CancellationToken cancellationToken)
    {
        var issues = new List<ReleaseValidationIssue>();
        var channel = await store.GetChannelAsync(scope, release.ChannelId, cancellationToken);
        if (channel is null)
        {
            AddError(issues, "release_channel_missing", "/channelId", "The release channel was not found.");
            channel = MissingChannel(scope, release.ChannelId);
        }
        else if (channel.Status != ReleaseChannelStatus.Active)
        {
            AddError(issues, "release_channel_archived", "/channelId", "The release channel is archived.");
        }

        var artifacts = new List<ReleaseArtifact>();
        var signingKeys = new Dictionary<Guid, ReleaseSigningKey>();
        if (release.ArtifactIds.Count == 0)
        {
            AddError(issues, "release_artifacts_required", "/artifactIds", "At least one artifact is required.");
        }
        for (var index = 0; index < release.ArtifactIds.Count; index++)
        {
            var artifactId = release.ArtifactIds[index];
            var artifact = await store.GetArtifactAsync(scope, artifactId, cancellationToken);
            if (artifact is null)
            {
                AddError(
                    issues,
                    "release_artifact_missing",
                    $"/artifactIds/{index}",
                    "The selected artifact was not found.");
                continue;
            }
            artifacts.Add(artifact);
            if (artifact.Status != ReleaseArtifactStatus.Verified)
            {
                AddError(
                    issues,
                    "release_artifact_not_verified",
                    $"/artifactIds/{index}",
                    "Every selected artifact must be verified and active.");
            }
            if (!string.Equals(artifact.ReleaseVersion, release.ReleaseVersion, StringComparison.Ordinal))
            {
                AddError(
                    issues,
                    "release_artifact_version_mismatch",
                    $"/artifactIds/{index}",
                    "The artifact version must match the release version.");
            }
            var key = await store.GetSigningKeyAsync(
                scope.TenantId,
                artifact.SigningKeyId,
                cancellationToken);
            if (key is null)
            {
                AddError(
                    issues,
                    "release_artifact_signing_key_missing",
                    $"/artifactIds/{index}",
                    "The artifact signing key was not found.");
                continue;
            }
            signingKeys[key.Id] = key;
            if (key.Status != ReleaseSigningKeyStatus.Active)
            {
                AddError(
                    issues,
                    "release_artifact_signing_key_archived",
                    $"/artifactIds/{index}",
                    "Artifact signing keys must remain active until publication.");
            }
            if (!ReleaseCryptography.VerifyDigestSignature(
                    key.PublicKeyPem,
                    artifact.Sha256,
                    artifact.Signature))
            {
                AddError(
                    issues,
                    "release_artifact_signature_invalid",
                    $"/artifactIds/{index}",
                    "The artifact signature is invalid.");
            }
        }

        foreach (var duplicate in artifacts
                     .GroupBy(static artifact => new
                     {
                         artifact.TargetRuntimeId,
                         artifact.ArtifactKind,
                         artifact.DeltaFromVersion,
                     })
                     .Where(static group => group.Count() > 1))
        {
            AddError(
                issues,
                "release_artifact_duplicate",
                "/artifactIds",
                $"Duplicate artifact mapping for runtime '{duplicate.Key.TargetRuntimeId}'.");
        }
        foreach (var runtime in artifacts
                     .GroupBy(static artifact => artifact.TargetRuntimeId, StringComparer.Ordinal))
        {
            if (!runtime.Any(static artifact => artifact.ArtifactKind == ReleaseArtifactKind.Full))
            {
                AddError(
                    issues,
                    "release_full_artifact_required",
                    "/artifactIds",
                    $"Runtime '{runtime.Key}' must have a full fallback artifact.");
            }
        }

        if (release.TargetSegmentId is { } segmentId)
        {
            var segment = await targetingStore.GetSegmentAsync(
                scope.TenantId,
                scope.ApplicationId,
                scope.EnvironmentId,
                segmentId,
                cancellationToken);
            if (segment is null)
            {
                AddError(issues, "release_segment_missing", "/targetSegmentId", "The target segment was not found.");
            }
            else if (segment.Status != TargetingResourceStatus.Active)
            {
                AddError(issues, "release_segment_archived", "/targetSegmentId", "The target segment is archived.");
            }
        }
        if (release.RolloutBasisPoints < TargetingContract.BucketCount)
        {
            issues.Add(new(
                ReleaseValidationSeverity.Warning,
                "release_partial_rollout",
                "/rolloutBasisPoints",
                "This release is configured for a partial rollout."));
        }

        var candidateArtifacts = artifacts
            .Where(artifact => signingKeys.ContainsKey(artifact.SigningKeyId))
            .ToArray();
        var candidate = ReleaseManifestBuilder.Build(
            release,
            channel,
            candidateArtifacts,
            signingKeys);
        return new(
            !issues.Any(static issue => issue.Severity == ReleaseValidationSeverity.Error),
            issues,
            candidate);
    }

    private async Task<ReleaseManifest> BuildManifestAsync(
        ReleaseScope scope,
        DesktopRelease release,
        CancellationToken cancellationToken)
    {
        var channel = await RequireChannelAsync(scope, release.ChannelId, cancellationToken);
        var artifacts = new List<ReleaseArtifact>(release.ArtifactIds.Count);
        var keys = new Dictionary<Guid, ReleaseSigningKey>();
        foreach (var artifactId in release.ArtifactIds)
        {
            var artifact = await RequireArtifactAsync(scope, artifactId, cancellationToken);
            artifacts.Add(artifact);
            if (!keys.ContainsKey(artifact.SigningKeyId))
            {
                keys[artifact.SigningKeyId] = await RequireSigningKeyAsync(
                    scope.TenantId,
                    artifact.SigningKeyId,
                    cancellationToken);
            }
        }
        return ReleaseManifestBuilder.Build(release, channel, artifacts, keys);
    }

    private async Task<ReleaseSigningKey> ChangeSigningKeyStatusAsync(
        string tenantId,
        string signingKeyId,
        long expectedVersion,
        ReleaseSigningKeyStatus desiredStatus,
        CancellationToken cancellationToken)
    {
        var tenant = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(tenant, desiredStatus == ReleaseSigningKeyStatus.Active, cancellationToken);
        var current = await RequireSigningKeyAsync(
            tenant,
            ParseId(signingKeyId, "signingKeyId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == desiredStatus)
        {
            return current;
        }
        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = desiredStatus,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = desiredStatus == ReleaseSigningKeyStatus.Archived ? now : null,
        };
        if (!await store.TryUpdateSigningKeyAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }
        return updated;
    }

    private async Task<ReleaseChannel> ChangeChannelStatusAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string channelId,
        long expectedVersion,
        ReleaseChannelStatus desiredStatus,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(
            scope,
            requireActive: desiredStatus == ReleaseChannelStatus.Active,
            cancellationToken);
        var current = await RequireChannelAsync(
            scope,
            ParseId(channelId, "channelId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == desiredStatus)
        {
            return current;
        }
        if (desiredStatus == ReleaseChannelStatus.Archived && current.ActiveReleaseId is not null)
        {
            throw FailedPrecondition(
                "release_channel_has_active_release",
                "Pause and roll back or otherwise remove the active release before archiving the channel.");
        }
        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = desiredStatus,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = desiredStatus == ReleaseChannelStatus.Archived ? now : null,
        };
        if (!await store.TryUpdateChannelAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }
        return updated;
    }

    private async Task RequireTenantAsync(
        Guid tenantId,
        bool requireActive,
        CancellationToken cancellationToken)
    {
        var tenant = await platformStore.GetTenantAsync(tenantId, cancellationToken)
            ?? throw NotFound("tenant_not_found", "The tenant was not found.");
        if (requireActive && tenant.Status != PlatformResourceStatus.Active)
        {
            throw FailedPrecondition("release_tenant_archived", "The tenant must be active.");
        }
    }

    private async Task RequireScopeAsync(
        ReleaseScope scope,
        bool requireActive,
        CancellationToken cancellationToken)
    {
        var tenant = await platformStore.GetTenantAsync(scope.TenantId, cancellationToken)
            ?? throw NotFound("tenant_not_found", "The tenant was not found.");
        var application = await platformStore.GetApplicationAsync(
            scope.TenantId,
            scope.ApplicationId,
            cancellationToken)
            ?? throw NotFound("application_not_found", "The application was not found.");
        var environment = await platformStore.GetEnvironmentAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            cancellationToken)
            ?? throw NotFound("environment_not_found", "The environment was not found.");
        if (requireActive
            && (tenant.Status != PlatformResourceStatus.Active
                || application.Status != PlatformResourceStatus.Active
                || environment.Status != PlatformResourceStatus.Active))
        {
            throw FailedPrecondition(
                "release_scope_archived",
                "The tenant, application, and environment must all be active.");
        }
    }

    private async Task<ReleaseSigningKey> RequireSigningKeyAsync(
        Guid tenantId,
        Guid signingKeyId,
        CancellationToken cancellationToken) =>
        await store.GetSigningKeyAsync(tenantId, signingKeyId, cancellationToken)
        ?? throw NotFound("release_signing_key_not_found", "The release signing key was not found.");

    private async Task<ReleaseChannel> RequireChannelAsync(
        ReleaseScope scope,
        Guid channelId,
        CancellationToken cancellationToken) =>
        await store.GetChannelAsync(scope, channelId, cancellationToken)
        ?? throw NotFound("release_channel_not_found", "The release channel was not found.");

    private async Task<ReleaseArtifact> RequireArtifactAsync(
        ReleaseScope scope,
        Guid artifactId,
        CancellationToken cancellationToken) =>
        await store.GetArtifactAsync(scope, artifactId, cancellationToken)
        ?? throw NotFound("release_artifact_not_found", "The release artifact was not found.");

    private async Task<DesktopRelease> RequireReleaseAsync(
        ReleaseScope scope,
        Guid releaseId,
        CancellationToken cancellationToken) =>
        await store.GetReleaseAsync(scope, releaseId, cancellationToken)
        ?? throw NotFound("desktop_release_not_found", "The desktop release was not found.");

    private static void RequireValid(ReleaseValidationResult validation)
    {
        if (validation.Valid)
        {
            return;
        }
        var fields = validation.Issues
            .Where(static issue => issue.Severity == ReleaseValidationSeverity.Error)
            .Take(20)
            .GroupBy(static issue => issue.Path, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<string>)group.Select(issue => issue.Message).ToArray(),
                StringComparer.Ordinal);
        throw new AsterloomException(
            AsterloomErrorKind.FailedPrecondition,
            "desktop_release_invalid",
            "The release must pass validation before publication.",
            fields);
    }

    private static void AddError(
        List<ReleaseValidationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(new(ReleaseValidationSeverity.Error, code, path, message));

    private static ReleaseChannel MissingChannel(ReleaseScope scope, Guid channelId) =>
        new(
            channelId,
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            "missing",
            "Missing",
            string.Empty,
            ReleaseChannelStatus.Archived,
            null,
            null,
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private static ReleaseScope ParseScope(
        string tenantId,
        string applicationId,
        string environmentId) =>
        new(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            ParseId(environmentId, "environmentId"));

    private static Guid ParseId(string? value, string field)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw Invalid(field, "A valid identifier is required.");
        }
        return id;
    }

    private static Guid? ParseOptionalId(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseId(value, field);

    private static Guid[] ParseArtifactIds(IReadOnlyCollection<string>? values)
    {
        if (values is null)
        {
            return [];
        }
        if (values.Count > 100)
        {
            throw Invalid("artifactIds", "A release may contain at most 100 artifacts.");
        }
        var result = values.Select(value => ParseId(value, "artifactIds")).ToArray();
        if (result.Distinct().Count() != result.Length)
        {
            throw Invalid("artifactIds", "Artifact identifiers must be unique.");
        }
        return result;
    }

    private static string NormalizeKey(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!KeyPattern().IsMatch(normalized))
        {
            throw Invalid(
                "key",
                "Use 1-100 lowercase letters, numbers, periods, underscores, or hyphens; start and end with a letter or number.");
        }
        return normalized;
    }

    private static string NormalizeDisplayName(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 200 || normalized.Any(char.IsControl))
        {
            throw Invalid(
                "displayName",
                "Display name must contain 1-200 characters without control characters.");
        }
        return normalized;
    }

    private static string NormalizeDescription(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 1_000 || normalized.Any(char.IsControl))
        {
            throw Invalid(
                "description",
                "Description must not exceed 1000 characters or contain control characters.");
        }
        return normalized;
    }

    private static string NormalizeReleaseNotes(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 50_000 || normalized.Contains('\0'))
        {
            throw Invalid(
                "releaseNotes",
                "Release notes must not exceed 50000 characters or contain null characters.");
        }
        return normalized;
    }

    private static string NormalizeRuntimeId(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!RuntimeIdPattern().IsMatch(normalized))
        {
            throw Invalid(
                "targetRuntimeId",
                "Use a valid 1-100 character .NET runtime identifier such as win-x64 or linux-arm64.");
        }
        return normalized;
    }

    private static string NormalizeFileName(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 255
            || normalized is "." or ".."
            || normalized.IndexOfAny(['/', '\\']) >= 0
            || normalized.Any(char.IsControl))
        {
            throw Invalid(
                "fileName",
                "File name must contain 1-255 characters and cannot contain path separators or control characters.");
        }
        return normalized;
    }

    private static string NormalizeContentType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ContentTypePattern().IsMatch(normalized))
        {
            throw Invalid("contentType", "A valid media type is required.");
        }
        return normalized;
    }

    private static string NormalizeSha256(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Sha256Pattern().IsMatch(normalized))
        {
            throw Invalid("sha256", "A lowercase or uppercase 64-character SHA-256 hex digest is required.");
        }
        return normalized;
    }

    private static string NormalizeSignature(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length is < 256 or > 1_024)
            {
                throw new FormatException();
            }
        }
        catch (FormatException)
        {
            throw Invalid("signature", "A valid RSA detached signature encoded as Base64 is required.");
        }
        return normalized;
    }

    private static string NormalizeVersion(string? value, string field)
    {
        if (!ReleaseVersion.TryParse(value, out var version))
        {
            throw Invalid(field, "A valid Semantic Version is required.");
        }
        return version.Original;
    }

    private static string NormalizeMinimumVersion(string? value, string releaseVersion)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "0.0.0"
            : NormalizeVersion(value, "minimumVersion");
        if (!ReleaseVersion.TryParse(normalized, out var minimum)
            || !ReleaseVersion.TryParse(releaseVersion, out var release))
        {
            throw Invalid("minimumVersion", "A valid Semantic Version is required.");
        }
        if (minimum.CompareTo(release) > 0)
        {
            throw Invalid("minimumVersion", "Minimum version cannot be newer than the release version.");
        }
        return normalized;
    }

    private static string NormalizeDeltaVersion(
        ReleaseArtifactKind artifactKind,
        string? deltaFromVersion,
        string releaseVersion)
    {
        if (!Enum.IsDefined(artifactKind))
        {
            throw Invalid("artifactKind", "Artifact kind must be Full or Delta.");
        }
        if (artifactKind == ReleaseArtifactKind.Full)
        {
            if (!string.IsNullOrWhiteSpace(deltaFromVersion))
            {
                throw Invalid("deltaFromVersion", "Full artifacts cannot specify a delta source version.");
            }
            return string.Empty;
        }

        var normalized = NormalizeVersion(deltaFromVersion, "deltaFromVersion");
        if (!ReleaseVersion.TryParse(normalized, out var source)
            || !ReleaseVersion.TryParse(releaseVersion, out var target))
        {
            throw Invalid("deltaFromVersion", "A valid Semantic Version is required.");
        }
        if (source.CompareTo(target) >= 0)
        {
            throw Invalid("deltaFromVersion", "A delta source version must be older than its target version.");
        }
        return normalized;
    }

    private static void RequireRollout(uint rolloutBasisPoints)
    {
        if (rolloutBasisPoints is < 1 or > TargetingContract.BucketCount)
        {
            throw Invalid(
                "rolloutBasisPoints",
                $"Rollout must be between 1 and {TargetingContract.BucketCount} basis points.");
        }
    }

    private static void RequireVersion(
        long currentVersion,
        long expectedVersion,
        string field = "expectedVersion")
    {
        if (expectedVersion <= 0)
        {
            throw Invalid(field, "Expected version must be positive.");
        }
        if (currentVersion != expectedVersion)
        {
            throw VersionConflict();
        }
    }

    private static void RequireDraft(DesktopRelease release)
    {
        if (release.Status != DesktopReleaseStatus.Draft)
        {
            throw FailedPrecondition(
                "desktop_release_not_draft",
                "Published releases are immutable; only a draft can be edited or published.");
        }
    }

    private static void RequireActive(ReleaseSigningKey signingKey)
    {
        if (signingKey.Status != ReleaseSigningKeyStatus.Active)
        {
            throw FailedPrecondition(
                "release_signing_key_archived",
                "The release signing key is archived and must be restored first.");
        }
    }

    private static void RequireActive(ReleaseChannel channel)
    {
        if (channel.Status != ReleaseChannelStatus.Active)
        {
            throw FailedPrecondition(
                "release_channel_archived",
                "The release channel is archived and must be restored first.");
        }
    }

    private static ReleasePageRequest CreatePageRequest(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeInactive)
    {
        var normalizedSize = pageSize == 0 ? DefaultPageSize : pageSize;
        if (normalizedSize is < 1 or > MaximumPageSize)
        {
            throw Invalid("pageSize", $"Page size must be between 1 and {MaximumPageSize}.");
        }
        var offset = 0;
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(pageToken));
                if (!int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                    || offset < 0)
                {
                    throw new FormatException();
                }
            }
            catch (FormatException)
            {
                throw Invalid("pageToken", "Page token is invalid.");
            }
        }
        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length > 200 || normalizedQuery.Any(char.IsControl))
        {
            throw Invalid("query", "Query must not exceed 200 characters or contain control characters.");
        }
        return new(offset, normalizedSize, normalizedQuery, includeInactive);
    }

    private static ReleaseListResult<T> ToListResult<T>(ReleaseStorePage<T> result, int offset) =>
        new(
            result.Items,
            result.HasMore
                ? WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
                    (offset + result.Items.Count).ToString(CultureInfo.InvariantCulture)))
                : string.Empty);

    private static AsterloomException Invalid(string field, string message) =>
        new(
            AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [field] = [message],
            });

    private static AsterloomException NotFound(string code, string message) =>
        new(AsterloomErrorKind.NotFound, code, message);

    private static AsterloomException AlreadyExists(string code, string message) =>
        new(AsterloomErrorKind.AlreadyExists, code, message);

    private static AsterloomException FailedPrecondition(string code, string message) =>
        new(AsterloomErrorKind.FailedPrecondition, code, message);

    private static AsterloomException VersionConflict() =>
        new(
            AsterloomErrorKind.Conflict,
            "version_conflict",
            "The resource changed since it was loaded. Reload and try again.");

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9.-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeIdPattern();

    [GeneratedRegex(
        "^[a-z0-9][a-z0-9!#$&^_.+-]{0,126}/[a-z0-9][a-z0-9!#$&^_.+-]{0,126}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ContentTypePattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
