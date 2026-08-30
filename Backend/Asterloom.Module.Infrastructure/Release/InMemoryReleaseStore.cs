using Asterloom.Modules.Release.Model;
using Asterloom.Modules.Release.Persistence;

namespace Asterloom.Modules.Infrastructure.Release;

internal sealed class InMemoryReleaseStore : IReleaseStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ReleaseSigningKey> _signingKeys = [];
    private readonly Dictionary<Guid, ReleaseChannel> _channels = [];
    private readonly Dictionary<Guid, ReleaseArtifact> _artifacts = [];
    private readonly Dictionary<Guid, DesktopRelease> _releases = [];

    public Task<ReleaseStorePage<ReleaseSigningKey>> ListSigningKeysAsync(
        Guid tenantId,
        ReleasePageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ToPage(
                _signingKeys.Values
                    .Where(item => item.TenantId == tenantId)
                    .Where(item => request.IncludeInactive
                        || item.Status == ReleaseSigningKeyStatus.Active)
                    .Where(item => Matches(request.Query, item.Key, item.DisplayName, item.Fingerprint))
                    .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.Id),
                request));
        }
    }

    public Task<ReleaseSigningKey?> GetSigningKeyAsync(
        Guid tenantId,
        Guid signingKeyId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _signingKeys.GetValueOrDefault(signingKeyId);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }

    public Task<bool> TryCreateSigningKeyAsync(
        ReleaseSigningKey signingKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_signingKeys.ContainsKey(signingKey.Id)
                || _signingKeys.Values.Any(item => item.TenantId == signingKey.TenantId
                    && (string.Equals(item.Key, signingKey.Key, StringComparison.Ordinal)
                        || string.Equals(item.Fingerprint, signingKey.Fingerprint, StringComparison.Ordinal))))
            {
                return Task.FromResult(false);
            }
            _signingKeys.Add(signingKey.Id, signingKey);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateSigningKeyAsync(
        ReleaseSigningKey signingKey,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_signingKeys.TryGetValue(signingKey.Id, out var current)
                || current.TenantId != signingKey.TenantId
                || current.Version != expectedVersion
                || !string.Equals(current.Key, signingKey.Key, StringComparison.Ordinal)
                || !string.Equals(current.Fingerprint, signingKey.Fingerprint, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }
            _signingKeys[signingKey.Id] = signingKey;
            return Task.FromResult(true);
        }
    }

    public Task<ReleaseStorePage<ReleaseChannel>> ListChannelsAsync(
        ReleaseScope scope,
        ReleasePageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ToPage(
                _channels.Values
                    .Where(item => MatchesScope(item, scope))
                    .Where(item => request.IncludeInactive
                        || item.Status == ReleaseChannelStatus.Active)
                    .Where(item => Matches(request.Query, item.Key, item.DisplayName, item.Description))
                    .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.Id),
                request));
        }
    }

    public Task<ReleaseChannel?> GetChannelAsync(
        ReleaseScope scope,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _channels.GetValueOrDefault(channelId);
            return Task.FromResult(item is not null && MatchesScope(item, scope) ? item : null);
        }
    }

    public Task<ReleaseChannel?> GetChannelByKeyAsync(
        ReleaseScope scope,
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_channels.Values.FirstOrDefault(item =>
                MatchesScope(item, scope)
                && string.Equals(item.Key, key, StringComparison.Ordinal)));
        }
    }

    public Task<bool> TryCreateChannelAsync(
        ReleaseChannel channel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_channels.ContainsKey(channel.Id)
                || _channels.Values.Any(item =>
                    MatchesScope(item, Scope(channel))
                    && string.Equals(item.Key, channel.Key, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }
            _channels.Add(channel.Id, channel);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateChannelAsync(
        ReleaseChannel channel,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!MatchesChannel(channel, expectedVersion))
            {
                return Task.FromResult(false);
            }
            _channels[channel.Id] = channel;
            return Task.FromResult(true);
        }
    }

    public Task<ReleaseStorePage<ReleaseArtifact>> ListArtifactsAsync(
        ReleaseScope scope,
        ReleasePageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ToPage(
                _artifacts.Values
                    .Where(item => MatchesScope(item, scope))
                    .Where(item => request.IncludeInactive
                        || item.Status != ReleaseArtifactStatus.Archived)
                    .Where(item => Matches(
                        request.Query,
                        item.ReleaseVersion,
                        item.TargetRuntimeId,
                        item.FileName,
                        item.Sha256))
                    .OrderByDescending(static item => item.CreatedAt)
                    .ThenBy(static item => item.Id),
                request));
        }
    }

    public Task<ReleaseArtifact?> GetArtifactAsync(
        ReleaseScope scope,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _artifacts.GetValueOrDefault(artifactId);
            return Task.FromResult(item is not null && MatchesScope(item, scope) ? item : null);
        }
    }

    public Task<ReleaseArtifact?> GetArtifactByIdentityAsync(
        ReleaseScope scope,
        string releaseVersion,
        string targetRuntimeId,
        ReleaseArtifactKind artifactKind,
        string deltaFromVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_artifacts.Values.FirstOrDefault(item =>
                MatchesScope(item, scope)
                && item.ArtifactKind == artifactKind
                && string.Equals(item.ReleaseVersion, releaseVersion, StringComparison.Ordinal)
                && string.Equals(item.TargetRuntimeId, targetRuntimeId, StringComparison.Ordinal)
                && string.Equals(item.DeltaFromVersion, deltaFromVersion, StringComparison.Ordinal)));
        }
    }

    public Task<bool> TryCreateArtifactAsync(
        ReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_artifacts.ContainsKey(artifact.Id)
                || _artifacts.Values.Any(item =>
                    MatchesScope(item, Scope(artifact))
                    && item.ArtifactKind == artifact.ArtifactKind
                    && string.Equals(item.ReleaseVersion, artifact.ReleaseVersion, StringComparison.Ordinal)
                    && string.Equals(item.TargetRuntimeId, artifact.TargetRuntimeId, StringComparison.Ordinal)
                    && string.Equals(item.DeltaFromVersion, artifact.DeltaFromVersion, StringComparison.Ordinal))
                || _artifacts.Values.Any(item =>
                    item.StorageObjectId == artifact.StorageObjectId
                    || item.UploadSessionId == artifact.UploadSessionId))
            {
                return Task.FromResult(false);
            }
            _artifacts.Add(artifact.Id, artifact);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateArtifactAsync(
        ReleaseArtifact artifact,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_artifacts.TryGetValue(artifact.Id, out var current)
                || current.Version != expectedVersion
                || !MatchesScope(current, Scope(artifact))
                || current.StorageObjectId != artifact.StorageObjectId
                || current.UploadSessionId != artifact.UploadSessionId)
            {
                return Task.FromResult(false);
            }
            _artifacts[artifact.Id] = artifact;
            return Task.FromResult(true);
        }
    }

    public Task<bool> IsArtifactReferencedByLiveReleaseAsync(
        ReleaseScope scope,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_releases.Values.Any(item =>
                MatchesScope(item, scope)
                && item.Status != DesktopReleaseStatus.RolledBack
                && item.ArtifactIds.Contains(artifactId)));
        }
    }

    public Task<ReleaseStorePage<DesktopRelease>> ListReleasesAsync(
        ReleaseScope scope,
        ReleasePageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ToPage(
                _releases.Values
                    .Where(item => MatchesScope(item, scope))
                    .Where(item => request.IncludeInactive
                        || item.Status != DesktopReleaseStatus.RolledBack)
                    .Where(item => Matches(
                        request.Query,
                        item.ReleaseVersion,
                        item.DisplayName,
                        item.ReleaseNotes))
                    .OrderByDescending(static item => item.UpdatedAt)
                    .ThenBy(static item => item.Id),
                request));
        }
    }

    public Task<DesktopRelease?> GetReleaseAsync(
        ReleaseScope scope,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _releases.GetValueOrDefault(releaseId);
            return Task.FromResult(item is not null && MatchesScope(item, scope) ? item : null);
        }
    }

    public Task<DesktopRelease?> GetReleaseByVersionAsync(
        ReleaseScope scope,
        Guid channelId,
        string releaseVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_releases.Values.FirstOrDefault(item =>
                MatchesScope(item, scope)
                && item.ChannelId == channelId
                && string.Equals(item.ReleaseVersion, releaseVersion, StringComparison.Ordinal)));
        }
    }

    public Task<bool> TryCreateReleaseAsync(
        DesktopRelease release,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_releases.ContainsKey(release.Id)
                || _releases.Values.Any(item =>
                    MatchesScope(item, Scope(release))
                    && item.ChannelId == release.ChannelId
                    && string.Equals(item.ReleaseVersion, release.ReleaseVersion, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }
            _releases.Add(release.Id, release);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateReleaseAsync(
        DesktopRelease release,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!MatchesRelease(release, expectedVersion))
            {
                return Task.FromResult(false);
            }
            _releases[release.Id] = release;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryPublishReleaseAsync(
        DesktopRelease release,
        long expectedReleaseVersion,
        ReleaseChannel channel,
        long expectedChannelVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!MatchesRelease(release, expectedReleaseVersion)
                || !MatchesChannel(channel, expectedChannelVersion)
                || release.ChannelId != channel.Id)
            {
                return Task.FromResult(false);
            }
            _releases[release.Id] = release;
            _channels[channel.Id] = channel;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryRollbackReleaseAsync(
        DesktopRelease currentRelease,
        long expectedCurrentVersion,
        DesktopRelease targetRelease,
        long expectedTargetVersion,
        ReleaseChannel channel,
        long expectedChannelVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (currentRelease.Id == targetRelease.Id
                || !MatchesRelease(currentRelease, expectedCurrentVersion)
                || !MatchesRelease(targetRelease, expectedTargetVersion)
                || !MatchesChannel(channel, expectedChannelVersion)
                || currentRelease.ChannelId != channel.Id
                || targetRelease.ChannelId != channel.Id)
            {
                return Task.FromResult(false);
            }
            _releases[currentRelease.Id] = currentRelease;
            _releases[targetRelease.Id] = targetRelease;
            _channels[channel.Id] = channel;
            return Task.FromResult(true);
        }
    }

    private bool MatchesChannel(ReleaseChannel channel, long expectedVersion) =>
        _channels.TryGetValue(channel.Id, out var current)
        && current.Version == expectedVersion
        && MatchesScope(current, Scope(channel))
        && string.Equals(current.Key, channel.Key, StringComparison.Ordinal);

    private bool MatchesRelease(DesktopRelease release, long expectedVersion) =>
        _releases.TryGetValue(release.Id, out var current)
        && current.Version == expectedVersion
        && MatchesScope(current, Scope(release))
        && current.ChannelId == release.ChannelId
        && string.Equals(current.ReleaseVersion, release.ReleaseVersion, StringComparison.Ordinal);

    private static bool Matches(string query, params string[] values) =>
        string.IsNullOrEmpty(query)
        || values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static ReleaseStorePage<T> ToPage<T>(
        IEnumerable<T> query,
        ReleasePageRequest request)
    {
        var items = query.Skip(request.Offset).Take(request.PageSize + 1).ToArray();
        return new(items.Take(request.PageSize).ToArray(), items.Length > request.PageSize);
    }

    private static ReleaseScope Scope(ReleaseChannel item) =>
        new(item.TenantId, item.ApplicationId, item.EnvironmentId);

    private static ReleaseScope Scope(ReleaseArtifact item) =>
        new(item.TenantId, item.ApplicationId, item.EnvironmentId);

    private static ReleaseScope Scope(DesktopRelease item) =>
        new(item.TenantId, item.ApplicationId, item.EnvironmentId);

    private static bool MatchesScope(ReleaseChannel item, ReleaseScope scope) =>
        item.TenantId == scope.TenantId
        && item.ApplicationId == scope.ApplicationId
        && item.EnvironmentId == scope.EnvironmentId;

    private static bool MatchesScope(ReleaseArtifact item, ReleaseScope scope) =>
        item.TenantId == scope.TenantId
        && item.ApplicationId == scope.ApplicationId
        && item.EnvironmentId == scope.EnvironmentId;

    private static bool MatchesScope(DesktopRelease item, ReleaseScope scope) =>
        item.TenantId == scope.TenantId
        && item.ApplicationId == scope.ApplicationId
        && item.EnvironmentId == scope.EnvironmentId;
}
