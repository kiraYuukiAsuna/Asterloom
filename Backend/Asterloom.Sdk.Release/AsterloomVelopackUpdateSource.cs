using System.Collections.Concurrent;
using Asterloom.Targeting;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Asterloom.Sdk.Release;

public sealed class AsterloomVelopackUpdateSource(
    AsterloomReleaseClient client,
    Func<string, TargetingEvaluationContext> contextFactory) : IUpdateSource
{
    private readonly ConcurrentDictionary<string, DownloadState> _downloads =
        new(StringComparer.Ordinal);

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

        var artifact = decision.SelectedArtifact!;
        var asset = new VelopackAsset
        {
            PackageId = string.IsNullOrWhiteSpace(appId)
                ? client.Options.PackageId
                : appId,
            Version = SemanticVersion.Parse(decision.Manifest!.ReleaseVersion),
            Type = artifact.ArtifactKind == AsterloomReleaseArtifactKind.Full
                ? VelopackAssetType.Full
                : VelopackAssetType.Delta,
            FileName = artifact.FileName,
            SHA1 = string.Empty,
            SHA256 = artifact.Sha256,
            Size = artifact.SizeBytes,
            NotesMarkdown = decision.Manifest.ReleaseNotes,
            NotesHTML = string.Empty,
        };
        _downloads[Key(asset)] = new(
            channel,
            currentVersion,
            context,
            decision,
            stagingId);
        return new VelopackAssetFeed { Assets = [asset] };
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
        if (decision.Download!.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(15))
        {
            decision = await client.CheckForUpdateAsync(
                state.Channel,
                state.CurrentVersion,
                state.Context,
                cancelToken).ConfigureAwait(false);
            var artifact = decision.SelectedArtifact;
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
        await client.DownloadToFileAsync(decision, localFile, progress, cancelToken)
            .ConfigureAwait(false);
    }

    private static string Key(VelopackAsset asset) =>
        $"{asset.FileName}\0{asset.SHA256}";

    private sealed record DownloadState(
        string Channel,
        string CurrentVersion,
        TargetingEvaluationContext Context,
        AsterloomUpdateDecision Decision,
        Guid? StagingId);
}
