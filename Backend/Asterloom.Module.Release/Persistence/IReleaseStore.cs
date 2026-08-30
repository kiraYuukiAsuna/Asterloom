using Asterloom.Modules.Release.Model;

namespace Asterloom.Modules.Release.Persistence;

public interface IReleaseStore
{
    Task<ReleaseStorePage<ReleaseSigningKey>> ListSigningKeysAsync(
        Guid tenantId,
        ReleasePageRequest request,
        CancellationToken cancellationToken);

    Task<ReleaseSigningKey?> GetSigningKeyAsync(
        Guid tenantId,
        Guid signingKeyId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateSigningKeyAsync(
        ReleaseSigningKey signingKey,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateSigningKeyAsync(
        ReleaseSigningKey signingKey,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<ReleaseStorePage<ReleaseChannel>> ListChannelsAsync(
        ReleaseScope scope,
        ReleasePageRequest request,
        CancellationToken cancellationToken);

    Task<ReleaseChannel?> GetChannelAsync(
        ReleaseScope scope,
        Guid channelId,
        CancellationToken cancellationToken);

    Task<ReleaseChannel?> GetChannelByKeyAsync(
        ReleaseScope scope,
        string key,
        CancellationToken cancellationToken);

    Task<bool> TryCreateChannelAsync(
        ReleaseChannel channel,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateChannelAsync(
        ReleaseChannel channel,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<ReleaseStorePage<ReleaseArtifact>> ListArtifactsAsync(
        ReleaseScope scope,
        ReleasePageRequest request,
        CancellationToken cancellationToken);

    Task<ReleaseArtifact?> GetArtifactAsync(
        ReleaseScope scope,
        Guid artifactId,
        CancellationToken cancellationToken);

    Task<ReleaseArtifact?> GetArtifactByIdentityAsync(
        ReleaseScope scope,
        string releaseVersion,
        string targetRuntimeId,
        ReleaseArtifactKind artifactKind,
        string deltaFromVersion,
        CancellationToken cancellationToken);

    Task<bool> TryCreateArtifactAsync(
        ReleaseArtifact artifact,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateArtifactAsync(
        ReleaseArtifact artifact,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<bool> IsArtifactReferencedByLiveReleaseAsync(
        ReleaseScope scope,
        Guid artifactId,
        CancellationToken cancellationToken);

    Task<ReleaseStorePage<DesktopRelease>> ListReleasesAsync(
        ReleaseScope scope,
        ReleasePageRequest request,
        CancellationToken cancellationToken);

    Task<DesktopRelease?> GetReleaseAsync(
        ReleaseScope scope,
        Guid releaseId,
        CancellationToken cancellationToken);

    Task<DesktopRelease?> GetReleaseByVersionAsync(
        ReleaseScope scope,
        Guid channelId,
        string releaseVersion,
        CancellationToken cancellationToken);

    Task<bool> TryCreateReleaseAsync(
        DesktopRelease release,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateReleaseAsync(
        DesktopRelease release,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<bool> TryPublishReleaseAsync(
        DesktopRelease release,
        long expectedReleaseVersion,
        ReleaseChannel channel,
        long expectedChannelVersion,
        CancellationToken cancellationToken);

    Task<bool> TryRollbackReleaseAsync(
        DesktopRelease currentRelease,
        long expectedCurrentVersion,
        DesktopRelease targetRelease,
        long expectedTargetVersion,
        ReleaseChannel channel,
        long expectedChannelVersion,
        CancellationToken cancellationToken);
}
