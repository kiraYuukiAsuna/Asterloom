using System.Collections.Concurrent;
using Asterloom.Targeting;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Asterloom.Sdk.Release;

public sealed class AsterloomVelopackUpdateSource(
    AsterloomReleaseClient client,
    Func<string, TargetingEvaluationContext> contextFactory,
    Action<VelopackAsset>? downloadCompleted) : IUpdateSource
{
    private readonly ConcurrentDictionary<string, DownloadState> _downloads =
        new(StringComparer.Ordinal);

    public AsterloomVelopackUpdateSource(
        AsterloomReleaseClient client,
        Func<string, TargetingEvaluationContext> contextFactory)
        : this(client, contextFactory, downloadCompleted: null)
    {
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId = null,
        VelopackAsset? latestLocalRelease = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var currentVersion = latestLocalRelease?.Version.ToFullString() ?? "0.0.0";
        var context = contextFactory(currentVersion)
            ?? throw new InvalidOperationException("The release context factory returned null.");
        var decision = await client.CheckForUpdateAsync(
            channel,
            currentVersion,
            context,
            CancellationToken.None).ConfigureAwait(false);
        if (!decision.UpdateAvailable)
        {
            return new VelopackAssetFeed { Assets = [] };
        }

        var downloads = AsterloomReleaseVerifier.GetArtifactDownloads(decision);
        if (decision.SelectedArtifact!.ArtifactKind == AsterloomReleaseArtifactKind.Delta
            && !downloads.Any(item =>
                item.Artifact.ArtifactKind == AsterloomReleaseArtifactKind.Full))
        {
            throw new AsterloomReleaseIntegrityException(
                "A delta update cannot be offered to Velopack without its target full-package fallback.");
        }

        var packageId = string.IsNullOrWhiteSpace(appId)
            ? client.Options.PackageId
            : appId;
        var version = SemanticVersion.Parse(decision.Manifest!.ReleaseVersion);
        var assets = new List<VelopackAsset>(downloads.Count);
        foreach (var delivery in downloads.OrderBy(static item => item.Artifact.ArtifactKind))
        {
            var artifact = delivery.Artifact;
            var asset = new VelopackAsset
            {
                PackageId = packageId,
                Version = version,
                Type = artifact.ArtifactKind == AsterloomReleaseArtifactKind.Full
                    ? VelopackAssetType.Full
                    : VelopackAssetType.Delta,
                FileName = artifact.FileName,
                SHA1 = string.Empty,
                // Velopack's package checksum path compares against its uppercase
                // hex output, so normalize the signed digest for its asset model.
                SHA256 = artifact.Sha256.ToUpperInvariant(),
                Size = artifact.SizeBytes,
                NotesMarkdown = decision.Manifest.ReleaseNotes,
                NotesHTML = string.Empty,
            };
            _downloads[Key(asset)] = new(
                channel,
                currentVersion,
                context,
                decision,
                artifact.Id,
                stagingId);
            assets.Add(asset);
        }
        return new VelopackAssetFeed { Assets = assets.ToArray() };
    }

    public async Task DownloadReleaseEntry(
        IVelopackLogger logger,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(releaseEntry);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFile);
        ArgumentNullException.ThrowIfNull(progress);
        if (!_downloads.TryGetValue(Key(releaseEntry), out var state))
        {
            throw new InvalidOperationException(
                "The Velopack asset was not obtained from this Asterloom update source.");
        }

        var decision = state.Decision;
        var delivery = AsterloomReleaseVerifier.GetArtifactDownloads(decision)
            .FirstOrDefault(item => item.Artifact.Id == state.ArtifactId)
            ?? throw new InvalidOperationException(
                "The selected Velopack asset is no longer present in the update decision.");
        if (delivery.Download.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(15))
        {
            decision = await client.CheckForUpdateAsync(
                state.Channel,
                state.CurrentVersion,
                state.Context,
                cancelToken).ConfigureAwait(false);
            delivery = AsterloomReleaseVerifier.GetArtifactDownloads(decision)
                .FirstOrDefault(item => item.Artifact.Id == state.ArtifactId);
            var artifact = delivery?.Artifact;
            if (!decision.UpdateAvailable
                || artifact is null
                || !string.Equals(artifact.FileName, releaseEntry.FileName, StringComparison.Ordinal)
                || !string.Equals(artifact.Sha256, releaseEntry.SHA256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The update decision changed before Velopack downloaded the selected asset.");
            }
            _downloads[Key(releaseEntry)] = state with { Decision = decision };
        }
        await client.DownloadArtifactToFileAsync(
                decision,
                state.ArtifactId,
                localFile,
                progress,
                cancelToken)
            .ConfigureAwait(false);
        downloadCompleted?.Invoke(releaseEntry);
    }

    private static string Key(VelopackAsset asset) =>
        $"{(int)asset.Type}\0{asset.FileName}\0{asset.SHA256}";

    private sealed record DownloadState(
        string Channel,
        string CurrentVersion,
        TargetingEvaluationContext Context,
        AsterloomUpdateDecision Decision,
        Guid ArtifactId,
        Guid? StagingId);
}
