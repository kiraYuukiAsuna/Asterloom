using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Config.Persistence;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;
using Asterloom.Targeting;

namespace Asterloom.Modules.Config;

public sealed class ConfigEvaluationService(
    IConfigStore store,
    ITargetingStore targetingStore,
    TimeProvider timeProvider)
{
    public async Task<ConfigEffectiveValue> PreviewAsync(
        ConfigEntry entry,
        bool useDraft,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken)
    {
        ValidateContext(entry.ApplicationId, entry.EnvironmentId, context);
        var definition = useDraft
            ? entry.DraftDefinition
            : entry.PublishedDefinition
                ?? throw FailedPrecondition(
                    "config_entry_unpublished",
                    "The configuration entry has no published revision.");
        var revision = useDraft ? entry.DraftRevision : entry.PublishedRevision!.Value;
        foreach (var rule in definition.TargetingRules)
        {
            var segment = await targetingStore.GetSegmentAsync(
                entry.TenantId,
                entry.ApplicationId,
                entry.EnvironmentId,
                rule.SegmentId,
                cancellationToken);
            if (segment?.Status == TargetingResourceStatus.Active
                && TargetingEvaluator.Evaluate(segment.Rule, context).Matched)
            {
                return new(
                    entry.Id,
                    entry.Key,
                    entry.ValueKind,
                    rule.Value,
                    revision,
                    ConfigEvaluationReason.TargetingMatch,
                    rule.Id);
            }
        }

        return new(
            entry.Id,
            entry.Key,
            entry.ValueKind,
            definition.DefaultValue,
            revision,
            ConfigEvaluationReason.Default,
            TargetingRuleId: null);
    }

    public async Task<ConfigSnapshotResult> GetSnapshotAsync(
        ConfigScope scope,
        TargetingEvaluationContext context,
        string? ifNoneMatch,
        bool includeServerValues,
        CancellationToken cancellationToken)
    {
        ValidateContext(scope.ApplicationId, scope.EnvironmentId, context);
        var snapshot = await store.GetLatestSnapshotAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            cancellationToken);
        var version = snapshot?.Version ?? 0;
        var etag = CreateETag(scope, version, context, includeServerValues);
        var now = timeProvider.GetUtcNow();
        if (MatchesETag(ifNoneMatch, etag))
        {
            return new(version, etag, NotModified: true, [], now);
        }

        var values = snapshot is null
            ? []
            : snapshot.Items
                .Where(item => includeServerValues || item.Visibility == ConfigVisibility.Client)
                .OrderBy(static item => item.Key, StringComparer.Ordinal)
                .Select(item => EvaluateSnapshotItem(item, context))
                .ToArray();
        return new(version, etag, NotModified: false, values, now);
    }

    public async Task<ConfigUpdateStatus> CheckUpdatesAsync(
        ConfigScope scope,
        long knownSnapshotVersion,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (knownSnapshotVersion < 0)
        {
            throw Invalid(
                "knownSnapshotVersion",
                "Known snapshot version cannot be negative.");
        }

        ValidateContext(scope.ApplicationId, scope.EnvironmentId, context);
        var snapshot = await store.GetLatestSnapshotAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            cancellationToken);
        var current = snapshot?.Version ?? 0;
        return new(
            current != knownSnapshotVersion,
            current,
            CreateETag(scope, current, context, includeServerValues: false),
            timeProvider.GetUtcNow());
    }

    public static string CreateETag(
        ConfigScope scope,
        long snapshotVersion,
        TargetingEvaluationContext context,
        bool includeServerValues)
    {
        var builder = new StringBuilder(512)
            .Append("v1\0")
            .Append(scope.TenantId.ToString("D"))
            .Append('\0')
            .Append(scope.ApplicationId.ToString("D"))
            .Append('\0')
            .Append(scope.EnvironmentId.ToString("D"))
            .Append('\0')
            .Append(snapshotVersion.ToString(CultureInfo.InvariantCulture))
            .Append('\0')
            .Append(includeServerValues ? "server" : "client")
            .Append('\0')
            .Append(context.TargetingKey)
            .Append('\0')
            .Append(context.UserId)
            .Append('\0')
            .Append(context.ClientVersion)
            .Append('\0')
            .Append(context.Platform)
            .Append('\0')
            .Append(context.Region)
            .Append('\0')
            .Append(context.Language);
        foreach (var attribute in context.Attributes.OrderBy(
                     static item => item.Key,
                     StringComparer.Ordinal))
        {
            builder.Append('\0').Append(attribute.Key).Append('=').Append(
                attribute.Value.Kind switch
                {
                    TargetingValueKind.Text => "s:" + attribute.Value.StringValue,
                    TargetingValueKind.Truth => "b:" + attribute.Value.BooleanValue,
                    TargetingValueKind.Numeric => "n:" + attribute.Value.NumberValue?.ToString(
                        "R",
                        CultureInfo.InvariantCulture),
                    _ => string.Empty,
                });
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
        return $"\"cfg-{snapshotVersion}-{digest[..24]}\"";
    }

    private static ConfigEffectiveValue EvaluateSnapshotItem(
        ConfigSnapshotItem item,
        TargetingEvaluationContext context)
    {
        foreach (var rule in item.TargetingRules)
        {
            if (TargetingEvaluator.Evaluate(rule.Rule, context).Matched)
            {
                return new(
                    item.EntryId,
                    item.Key,
                    item.ValueKind,
                    rule.Value,
                    item.Revision,
                    ConfigEvaluationReason.TargetingMatch,
                    rule.Id);
            }
        }

        return new(
            item.EntryId,
            item.Key,
            item.ValueKind,
            item.Definition.DefaultValue,
            item.Revision,
            ConfigEvaluationReason.Default,
            TargetingRuleId: null);
    }

    private static bool MatchesETag(string? candidate, string etag) =>
        !string.IsNullOrWhiteSpace(candidate)
        && (string.Equals(candidate.Trim(), etag, StringComparison.Ordinal)
            || candidate.Split(',').Select(static item => item.Trim()).Contains(
                etag,
                StringComparer.Ordinal));

    private static void ValidateContext(
        Guid applicationId,
        Guid environmentId,
        TargetingEvaluationContext context)
    {
        if (context.ApplicationId != applicationId || context.EnvironmentId != environmentId)
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

    private static AsterloomException Invalid(string field, string message) =>
        new(
            AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [field] = [message],
            });

    private static AsterloomException FailedPrecondition(string code, string message) =>
        new(AsterloomErrorKind.FailedPrecondition, code, message);
}
