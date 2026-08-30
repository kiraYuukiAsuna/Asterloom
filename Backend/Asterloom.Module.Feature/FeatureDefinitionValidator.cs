using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asterloom.Modules.Feature.Model;
using Asterloom.Modules.Feature.Persistence;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;
using Asterloom.Targeting;

namespace Asterloom.Modules.Feature;

public sealed partial class FeatureDefinitionValidator(
    IFeatureStore featureStore,
    ITargetingStore targetingStore)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<FeatureValidationResult> ValidateAsync(
        FeatureFlag flag,
        FeatureDefinition definition,
        CancellationToken cancellationToken)
    {
        var issues = ValidateShape(flag.ValueKind, definition).ToList();
        var variants = definition.Variants
            .GroupBy(static variant => variant.Key, StringComparer.Ordinal)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.Ordinal);

        await ValidatePrerequisitesAsync(flag, definition, issues, cancellationToken);
        await ValidateTargetingRulesAsync(flag, definition, variants, issues, cancellationToken);

        return new(
            !issues.Any(static issue => issue.Severity == FeatureValidationSeverity.Error),
            issues,
            ComputeHash(definition));
    }

    public static void EnsureDraftSafety(FeatureDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Variants);
        ArgumentNullException.ThrowIfNull(definition.Prerequisites);
        ArgumentNullException.ThrowIfNull(definition.TargetingRules);
        ArgumentNullException.ThrowIfNull(definition.Allocations);
        if (definition.Variants.Count > 50
            || definition.Prerequisites.Count > 20
            || definition.TargetingRules.Count > 50
            || definition.Allocations.Count > 100)
        {
            throw new ArgumentException("The feature definition exceeds its safety limits.");
        }

        if (definition.BucketingSalt.Length is < 1 or > 500
            || definition.BucketingSalt.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Bucketing salt must contain 1-500 characters without control characters.");
        }
    }

    public static string ComputeHash(FeatureDefinition definition)
    {
        var json = JsonSerializer.Serialize(definition, SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static IEnumerable<FeatureValidationIssue> ValidateShape(
        FeatureValueKind valueKind,
        FeatureDefinition definition)
    {
        FeatureValidationIssue Error(string code, string path, string message) =>
            new(FeatureValidationSeverity.Error, code, path, message);
        FeatureValidationIssue Warning(string code, string path, string message) =>
            new(FeatureValidationSeverity.Warning, code, path, message);

        var safetyError = GetDraftSafetyError(definition);
        if (safetyError is not null)
        {
            yield return Error("definition_limits", "definition", safetyError);
            yield break;
        }

        if (definition.Variants.Count is < 1 or > 20)
        {
            yield return Error(
                "variant_count",
                "variants",
                "A published flag must define between 1 and 20 variants.");
        }

        var variantKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < definition.Variants.Count; index++)
        {
            var variant = definition.Variants[index];
            var path = $"variants[{index}]";
            if (!KeyPattern().IsMatch(variant.Key))
            {
                yield return Error(
                    "variant_key_invalid",
                    $"{path}.key",
                    "Use a stable lowercase variant key containing at most 100 characters.");
            }
            else if (!variantKeys.Add(variant.Key))
            {
                yield return Error(
                    "variant_key_duplicate",
                    $"{path}.key",
                    $"Variant key '{variant.Key}' is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(variant.DisplayName)
                || variant.DisplayName.Length > 200
                || variant.DisplayName.Any(char.IsControl))
            {
                yield return Error(
                    "variant_name_invalid",
                    $"{path}.displayName",
                    "Variant display name must contain 1-200 characters.");
            }

            if (variant.Value.Kind != valueKind)
            {
                yield return Error(
                    "variant_type_mismatch",
                    $"{path}.value",
                    $"Variant '{variant.Key}' must contain a {valueKind} value.");
            }
        }

        if (!variantKeys.Contains(definition.DefaultVariantKey))
        {
            yield return Error(
                "default_variant_missing",
                "defaultVariantKey",
                "Default variant must reference a defined variant.");
        }

        var prerequisiteKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < definition.Prerequisites.Count; index++)
        {
            var prerequisite = definition.Prerequisites[index];
            if (!KeyPattern().IsMatch(prerequisite.FlagKey))
            {
                yield return Error(
                    "prerequisite_key_invalid",
                    $"prerequisites[{index}].flagKey",
                    "Prerequisite flag key is invalid.");
            }
            else if (!prerequisiteKeys.Add(prerequisite.FlagKey))
            {
                yield return Error(
                    "prerequisite_duplicate",
                    $"prerequisites[{index}].flagKey",
                    $"Prerequisite '{prerequisite.FlagKey}' is duplicated.");
            }
        }

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < definition.TargetingRules.Count; index++)
        {
            var rule = definition.TargetingRules[index];
            if (!RuleIdPattern().IsMatch(rule.Id) || !ruleIds.Add(rule.Id))
            {
                yield return Error(
                    "targeting_rule_id_invalid",
                    $"targetingRules[{index}].id",
                    "Targeting rule IDs must be unique stable identifiers.");
            }

            if (rule.SegmentId == Guid.Empty)
            {
                yield return Error(
                    "targeting_segment_invalid",
                    $"targetingRules[{index}].segmentId",
                    "A valid targeting segment is required.");
            }

            if (!variantKeys.Contains(rule.VariantKey))
            {
                yield return Error(
                    "targeting_variant_missing",
                    $"targetingRules[{index}].variantKey",
                    "Targeting rule variant must reference a defined variant.");
            }
        }

        var allocations = definition.Allocations
            .Select(static allocation => new TargetingBucketAllocation(
                allocation.VariantKey,
                allocation.Start,
                allocation.End))
            .ToArray();
        var allocationError = GetAllocationError(allocations);
        if (allocationError is not null)
        {
            yield return Error("allocation_invalid", "allocations", allocationError);
        }

        for (var index = 0; index < definition.Allocations.Count; index++)
        {
            if (!variantKeys.Contains(definition.Allocations[index].VariantKey))
            {
                yield return Error(
                    "allocation_variant_missing",
                    $"allocations[{index}].variantKey",
                    "Allocation must reference a defined variant.");
            }
        }

        if (definition.Allocations.Count > 0)
        {
            var ordered = definition.Allocations.OrderBy(static item => item.Start).ToArray();
            uint previousEnd = 0;
            var hasGap = false;
            foreach (var allocation in ordered)
            {
                hasGap |= allocation.Start > previousEnd;
                previousEnd = Math.Max(previousEnd, allocation.End);
            }

            hasGap |= previousEnd < TargetingContract.BucketCount;
            if (hasGap)
            {
                yield return Warning(
                    "allocation_gap",
                    "allocations",
                    "Unallocated buckets fall back to the default variant.");
            }
        }
    }

    private async Task ValidatePrerequisitesAsync(
        FeatureFlag flag,
        FeatureDefinition definition,
        List<FeatureValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < definition.Prerequisites.Count; index++)
        {
            var prerequisite = definition.Prerequisites[index];
            var path = $"prerequisites[{index}]";
            if (string.Equals(prerequisite.FlagKey, flag.Key, StringComparison.Ordinal))
            {
                issues.Add(Error("prerequisite_self", path, "A flag cannot depend on itself."));
                continue;
            }

            var dependency = await featureStore.GetFlagByKeyAsync(
                flag.TenantId,
                flag.ApplicationId,
                flag.EnvironmentId,
                prerequisite.FlagKey,
                cancellationToken);
            if (dependency?.PublishedDefinition is null
                || dependency.Status != FeatureResourceStatus.Active)
            {
                issues.Add(Error(
                    "prerequisite_unpublished",
                    path,
                    $"Prerequisite '{prerequisite.FlagKey}' must be active and published."));
                continue;
            }

            if (!dependency.PublishedDefinition.Variants.Any(variant =>
                    string.Equals(
                        variant.Key,
                        prerequisite.ExpectedVariantKey,
                        StringComparison.Ordinal)))
            {
                issues.Add(Error(
                    "prerequisite_variant_missing",
                    $"{path}.expectedVariantKey",
                    "Expected prerequisite variant does not exist in its published revision."));
            }

            if (await ReferencesFlagAsync(
                    flag,
                    dependency,
                    new HashSet<string>(StringComparer.Ordinal) { flag.Key },
                    cancellationToken))
            {
                issues.Add(Error(
                    "prerequisite_cycle",
                    path,
                    $"Prerequisite '{prerequisite.FlagKey}' creates a dependency cycle."));
            }
        }
    }

    private async Task<bool> ReferencesFlagAsync(
        FeatureFlag root,
        FeatureFlag current,
        HashSet<string> visited,
        CancellationToken cancellationToken)
    {
        if (!visited.Add(current.Key) || current.PublishedDefinition is null)
        {
            return false;
        }

        foreach (var prerequisite in current.PublishedDefinition.Prerequisites)
        {
            if (string.Equals(prerequisite.FlagKey, root.Key, StringComparison.Ordinal))
            {
                return true;
            }

            if (visited.Count >= 64)
            {
                return true;
            }

            var next = await featureStore.GetFlagByKeyAsync(
                root.TenantId,
                root.ApplicationId,
                root.EnvironmentId,
                prerequisite.FlagKey,
                cancellationToken);
            if (next is not null
                && await ReferencesFlagAsync(root, next, visited, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task ValidateTargetingRulesAsync(
        FeatureFlag flag,
        FeatureDefinition definition,
        Dictionary<string, FeatureVariant> variants,
        List<FeatureValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < definition.TargetingRules.Count; index++)
        {
            var rule = definition.TargetingRules[index];
            if (rule.SegmentId == Guid.Empty || !variants.ContainsKey(rule.VariantKey))
            {
                continue;
            }

            var segment = await targetingStore.GetSegmentAsync(
                flag.TenantId,
                flag.ApplicationId,
                flag.EnvironmentId,
                rule.SegmentId,
                cancellationToken);
            if (segment?.Status != TargetingResourceStatus.Active)
            {
                issues.Add(Error(
                    "targeting_segment_unavailable",
                    $"targetingRules[{index}].segmentId",
                    "Targeting segment must exist and be active in the same environment."));
            }
        }
    }

    private static FeatureValidationIssue Error(string code, string path, string message) =>
        new(FeatureValidationSeverity.Error, code, path, message);

    private static string? GetDraftSafetyError(FeatureDefinition definition)
    {
        try
        {
            EnsureDraftSafety(definition);
            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    private static string? GetAllocationError(
        TargetingBucketAllocation[] allocations)
    {
        try
        {
            if (allocations.Length > 0)
            {
                _ = TargetingContract.SelectBucketAllocation(0, allocations);
            }

            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex(
        "^[a-zA-Z0-9](?:[a-zA-Z0-9._-]{0,98}[a-zA-Z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RuleIdPattern();
}
