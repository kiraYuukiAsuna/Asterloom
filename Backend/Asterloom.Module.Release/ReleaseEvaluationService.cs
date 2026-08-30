using System.Text.RegularExpressions;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Asterloom.Modules.Release.Model;
using Asterloom.Modules.Release.Persistence;
using Asterloom.Modules.Storage;
using Asterloom.Modules.Storage.Model;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;
using Asterloom.Targeting;

namespace Asterloom.Modules.Release;

public sealed partial class ReleaseEvaluationService(
    IReleaseStore store,
    IPlatformResourceStore platformStore,
    ITargetingStore targetingStore,
    StorageManagementService storage,
    ReleaseManagementService managementService)
{
    public async Task<UpdateDecision> CheckForUpdateAsync(
        UpdateCheckRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireActiveScopeAsync(request.Scope, cancellationToken);
        ValidateContext(request.Scope, request.Context);
        var channelKey = NormalizeKey(request.ChannelKey, "channelKey");
        var targetRuntimeId = NormalizeRuntimeId(request.TargetRuntimeId);
        if (!ReleaseVersion.TryParse(request.CurrentVersion, out var currentVersion))
        {
            throw Invalid("currentVersion", "A valid Semantic Version is required.");
        }

        var channel = await store.GetChannelByKeyAsync(
            request.Scope,
            channelKey,
            cancellationToken)
            ?? throw NotFound("release_channel_not_found", "The release channel was not found.");
        if (channel.Status != ReleaseChannelStatus.Active)
        {
            throw FailedPrecondition(
                "release_channel_archived",
                "The release channel is archived.");
        }
        if (channel.ActiveReleaseId is null)
        {
            return NoUpdate(
                UpdateDecisionReason.ChannelEmpty,
                channel,
                release: null,
                bucketEvaluated: false,
                bucket: 0,
                rolloutBasisPoints: 0,
                ["channel_has_no_active_release"]);
        }

        var release = await store.GetReleaseAsync(
            request.Scope,
            channel.ActiveReleaseId.Value,
            cancellationToken);
        if (release is null || release.Status is DesktopReleaseStatus.Draft or DesktopReleaseStatus.RolledBack)
        {
            return NoUpdate(
                UpdateDecisionReason.ChannelEmpty,
                channel,
                release,
                bucketEvaluated: false,
                bucket: 0,
                rolloutBasisPoints: release?.RolloutBasisPoints ?? 0,
                ["channel_active_release_unavailable"]);
        }
        if (release.Status == DesktopReleaseStatus.Paused)
        {
            return NoUpdate(
                UpdateDecisionReason.ReleasePaused,
                channel,
                release,
                bucketEvaluated: false,
                bucket: 0,
                release.RolloutBasisPoints,
                ["release_paused"]);
        }
        if (!ReleaseVersion.TryParse(release.ReleaseVersion, out var targetVersion)
            || !ReleaseVersion.TryParse(release.MinimumVersion, out var minimumVersion))
        {
            throw FailedPrecondition(
                "desktop_release_version_invalid",
                "The published release contains invalid version metadata.");
        }
        if (currentVersion.CompareTo(targetVersion) >= 0)
        {
            return NoUpdate(
                UpdateDecisionReason.Current,
                channel,
                release,
                bucketEvaluated: false,
                bucket: 0,
                release.RolloutBasisPoints,
                ["client_version_is_current"]);
        }

        var trace = new List<string>();
        if (release.TargetSegmentId is { } segmentId)
        {
            var segment = await targetingStore.GetSegmentAsync(
                request.Scope.TenantId,
                request.Scope.ApplicationId,
                request.Scope.EnvironmentId,
                segmentId,
                cancellationToken);
            if (segment is null || segment.Status != TargetingResourceStatus.Active)
            {
                return NoUpdate(
                    UpdateDecisionReason.TargetingMiss,
                    channel,
                    release,
                    bucketEvaluated: false,
                    bucket: 0,
                    release.RolloutBasisPoints,
                    ["target_segment_unavailable"]);
            }
            var segmentResult = TargetingEvaluator.Evaluate(segment.Rule, request.Context);
            trace.Add($"segment:{segment.Key}:{(segmentResult.Matched ? "matched" : "not_matched")}");
            trace.AddRange(segmentResult.Conditions.Select(condition =>
                $"condition:{condition.ConditionId}:{condition.Reason.ToString().ToLowerInvariant()}"));
            if (!segmentResult.Matched)
            {
                return NoUpdate(
                    UpdateDecisionReason.TargetingMiss,
                    channel,
                    release,
                    bucketEvaluated: false,
                    bucket: 0,
                    release.RolloutBasisPoints,
                    trace);
            }
        }
        else
        {
            trace.Add("segment:not_configured");
        }

        var bucketNamespace = TargetingContract.CreateBucketNamespace(
            "release",
            release.Id.ToString("N"),
            request.Scope.EnvironmentId);
        var bucket = TargetingContract.ComputeBucket(
            bucketNamespace,
            release.BucketingSalt,
            request.Context.TargetingKey);
        trace.Add($"bucket:{bucket}:{release.RolloutBasisPoints}");
        if (bucket >= release.RolloutBasisPoints)
        {
            return NoUpdate(
                UpdateDecisionReason.RolloutExcluded,
                channel,
                release,
                bucketEvaluated: true,
                bucket,
                release.RolloutBasisPoints,
                trace);
        }

        var runtimeArtifacts = new List<ReleaseArtifact>();
        foreach (var artifactId in release.ArtifactIds)
        {
            var artifact = await store.GetArtifactAsync(request.Scope, artifactId, cancellationToken);
            if (artifact is not null
                && artifact.Status == ReleaseArtifactStatus.Verified
                && string.Equals(
                    artifact.TargetRuntimeId,
                    targetRuntimeId,
                    StringComparison.Ordinal))
            {
                runtimeArtifacts.Add(artifact);
            }
        }
        var belowMinimum = currentVersion.CompareTo(minimumVersion) < 0;
        var selected = belowMinimum
            ? null
            : runtimeArtifacts.FirstOrDefault(artifact =>
                artifact.ArtifactKind == ReleaseArtifactKind.Delta
                && string.Equals(
                    artifact.DeltaFromVersion,
                    currentVersion.Original,
                    StringComparison.Ordinal));
        selected ??= runtimeArtifacts.FirstOrDefault(
            static artifact => artifact.ArtifactKind == ReleaseArtifactKind.Full);
        if (selected is null)
        {
            trace.Add("artifact:no_compatible_artifact");
            return NoUpdate(
                UpdateDecisionReason.NoCompatibleArtifact,
                channel,
                release,
                bucketEvaluated: true,
                bucket,
                release.RolloutBasisPoints,
                trace);
        }
        trace.Add($"artifact:{selected.ArtifactKind.ToString().ToLowerInvariant()}:{selected.TargetRuntimeId}");

        var manifest = await managementService.GetReleaseManifestAsync(
            request.Scope.TenantId.ToString("D"),
            request.Scope.ApplicationId.ToString("D"),
            request.Scope.EnvironmentId.ToString("D"),
            release.Id.ToString("D"),
            cancellationToken);
        StorageTransferTicket download = await storage.CreateDownloadUrlAsync(
            request.Scope.TenantId.ToString("D"),
            selected.StorageBucketId.ToString("D"),
            selected.StorageObjectId.ToString("D"),
            lifetimeSeconds: 300,
            cancellationToken);
        return new UpdateDecision(
            UpdateAvailable: true,
            UpdateDecisionReason.UpdateAvailable,
            channel,
            release,
            manifest,
            selected,
            download,
            Mandatory: release.Mandatory || belowMinimum,
            BucketEvaluated: true,
            bucket,
            release.RolloutBasisPoints,
            trace);
    }

    private async Task RequireActiveScopeAsync(
        ReleaseScope scope,
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
        if (tenant.Status != PlatformResourceStatus.Active
            || application.Status != PlatformResourceStatus.Active
            || environment.Status != PlatformResourceStatus.Active)
        {
            throw FailedPrecondition(
                "release_scope_archived",
                "The tenant, application, and environment must all be active.");
        }
    }

    private static void ValidateContext(ReleaseScope scope, TargetingEvaluationContext context)
    {
        if (context is null)
        {
            throw Invalid("context", "An evaluation context is required.");
        }
        if (context.ApplicationId != scope.ApplicationId
            || context.EnvironmentId != scope.EnvironmentId)
        {
            throw Invalid(
                "context",
                "The evaluation context must use the application and environment from the route.");
        }
        try
        {
            TargetingContract.ValidateContext(context);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("context", exception.Message);
        }
    }

    private static UpdateDecision NoUpdate(
        UpdateDecisionReason reason,
        ReleaseChannel? channel,
        DesktopRelease? release,
        bool bucketEvaluated,
        uint bucket,
        uint rolloutBasisPoints,
        IReadOnlyList<string> trace) =>
        new(
            UpdateAvailable: false,
            reason,
            channel,
            release,
            Manifest: null,
            SelectedArtifact: null,
            Download: null,
            Mandatory: false,
            bucketEvaluated,
            bucket,
            rolloutBasisPoints,
            trace);

    private static string NormalizeKey(string? value, string field)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IdentifierPattern().IsMatch(normalized))
        {
            throw Invalid(
                field,
                "Use 1-100 lowercase letters, numbers, periods, underscores, or hyphens; start and end with a letter or number.");
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

    private static AsterloomException FailedPrecondition(string code, string message) =>
        new(AsterloomErrorKind.FailedPrecondition, code, message);

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9.-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeIdPattern();
}
