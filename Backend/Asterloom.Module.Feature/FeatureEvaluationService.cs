using Asterloom.Modules.Errors;
using Asterloom.Modules.Feature.Model;
using Asterloom.Modules.Feature.Persistence;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;
using Asterloom.Targeting;

namespace Asterloom.Modules.Feature;

public sealed class FeatureEvaluationService(
    IFeatureStore featureStore,
    ITargetingStore targetingStore)
{
    public async Task<FeatureEvaluationDetails> EvaluateAsync(
        FeatureEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateContextScope(request.Scope, request.Context);
        var flag = request.FlagId is { } flagId
            ? await featureStore.GetFlagAsync(
                request.Scope.TenantId,
                request.Scope.ApplicationId,
                request.Scope.EnvironmentId,
                flagId,
                cancellationToken)
            : await featureStore.GetFlagByKeyAsync(
                request.Scope.TenantId,
                request.Scope.ApplicationId,
                request.Scope.EnvironmentId,
                request.FlagKey,
                cancellationToken);
        if (flag is null)
        {
            throw NotFound("feature_flag_not_found", "The feature flag was not found.");
        }

        if (flag.Status != FeatureResourceStatus.Active)
        {
            throw FailedPrecondition("feature_flag_archived", "The feature flag is archived.");
        }

        if (request.ExpectedKind is { } expectedKind && flag.ValueKind != expectedKind)
        {
            throw FailedPrecondition(
                "feature_type_mismatch",
                $"The flag is {flag.ValueKind}, not {expectedKind}.");
        }

        var definition = request.UseDraft
            ? flag.DraftDefinition
            : flag.PublishedDefinition
                ?? throw FailedPrecondition(
                    "feature_flag_unpublished",
                    "The feature flag has no published revision.");
        var revision = request.UseDraft
            ? flag.DraftRevision
            : flag.PublishedRevision!.Value;
        return await EvaluateDefinitionAsync(
            flag,
            definition,
            revision,
            request.Context,
            request.UseDraft,
            new HashSet<string>(StringComparer.Ordinal),
            cancellationToken);
    }

    private async Task<FeatureEvaluationDetails> EvaluateDefinitionAsync(
        FeatureFlag flag,
        FeatureDefinition definition,
        long revision,
        TargetingEvaluationContext context,
        bool usedDraft,
        HashSet<string> evaluationStack,
        CancellationToken cancellationToken)
    {
        if (!evaluationStack.Add(flag.Key))
        {
            throw FailedPrecondition(
                "feature_prerequisite_cycle",
                "A feature prerequisite cycle was detected.");
        }

        try
        {
            var variants = definition.Variants.ToDictionary(
                static variant => variant.Key,
                StringComparer.Ordinal);
            if (!variants.TryGetValue(definition.DefaultVariantKey, out var defaultVariant))
            {
                throw FailedPrecondition(
                    "feature_definition_invalid",
                    "The feature definition has no valid default variant.");
            }

            var trace = new List<string>();
            if (!definition.Enabled)
            {
                trace.Add("flag disabled; returned default variant");
                return Details(
                    flag,
                    revision,
                    defaultVariant,
                    FeatureEvaluationReason.Disabled,
                    trace,
                    usedDraft);
            }

            foreach (var prerequisite in definition.Prerequisites)
            {
                var dependency = await featureStore.GetFlagByKeyAsync(
                    flag.TenantId,
                    flag.ApplicationId,
                    flag.EnvironmentId,
                    prerequisite.FlagKey,
                    cancellationToken);
                if (dependency?.PublishedDefinition is null
                    || dependency.Status != FeatureResourceStatus.Active)
                {
                    trace.Add($"prerequisite {prerequisite.FlagKey} unavailable");
                    return Details(
                        flag,
                        revision,
                        defaultVariant,
                        FeatureEvaluationReason.PrerequisiteFailed,
                        trace,
                        usedDraft);
                }

                var dependencyResult = await EvaluateDefinitionAsync(
                    dependency,
                    dependency.PublishedDefinition,
                    dependency.PublishedRevision!.Value,
                    context,
                    usedDraft: false,
                    evaluationStack,
                    cancellationToken);
                if (!string.Equals(
                        dependencyResult.VariantKey,
                        prerequisite.ExpectedVariantKey,
                        StringComparison.Ordinal))
                {
                    trace.Add($"prerequisite {prerequisite.FlagKey} did not match expected variant");
                    return Details(
                        flag,
                        revision,
                        defaultVariant,
                        FeatureEvaluationReason.PrerequisiteFailed,
                        trace,
                        usedDraft);
                }

                trace.Add($"prerequisite {prerequisite.FlagKey} matched");
            }

            foreach (var targetingRule in definition.TargetingRules)
            {
                var segment = await targetingStore.GetSegmentAsync(
                    flag.TenantId,
                    flag.ApplicationId,
                    flag.EnvironmentId,
                    targetingRule.SegmentId,
                    cancellationToken);
                if (segment?.Status != TargetingResourceStatus.Active)
                {
                    trace.Add($"rule {targetingRule.Id} skipped because its segment is unavailable");
                    continue;
                }

                var segmentResult = TargetingEvaluator.Evaluate(segment.Rule, context);
                trace.Add(
                    $"rule {targetingRule.Id} "
                    + (segmentResult.Matched ? "matched" : "did not match"));
                if (segmentResult.Matched
                    && variants.TryGetValue(targetingRule.VariantKey, out var ruleVariant))
                {
                    return Details(
                        flag,
                        revision,
                        ruleVariant,
                        FeatureEvaluationReason.TargetingMatch,
                        trace,
                        usedDraft);
                }
            }

            if (definition.Allocations.Count > 0)
            {
                var bucketNamespace = TargetingContract.CreateBucketNamespace(
                    "feature",
                    flag.Key,
                    flag.EnvironmentId);
                var bucket = TargetingContract.ComputeBucket(
                    bucketNamespace,
                    definition.BucketingSalt,
                    context.TargetingKey);
                var selectedKey = TargetingContract.SelectBucketAllocation(
                    bucket,
                    definition.Allocations.Select(static allocation =>
                        new TargetingBucketAllocation(
                            allocation.VariantKey,
                            allocation.Start,
                            allocation.End)).ToArray());
                trace.Add(selectedKey is null
                    ? $"bucket {bucket} was unallocated"
                    : $"bucket {bucket} selected an allocation");
                if (selectedKey is not null && variants.TryGetValue(selectedKey, out var variant))
                {
                    return Details(
                        flag,
                        revision,
                        variant,
                        FeatureEvaluationReason.Split,
                        trace,
                        usedDraft,
                        bucketEvaluated: true,
                        bucket);
                }

                return Details(
                    flag,
                    revision,
                    defaultVariant,
                    FeatureEvaluationReason.Default,
                    trace,
                    usedDraft,
                    bucketEvaluated: true,
                    bucket);
            }

            trace.Add("returned default variant");
            return Details(
                flag,
                revision,
                defaultVariant,
                FeatureEvaluationReason.Default,
                trace,
                usedDraft);
        }
        finally
        {
            evaluationStack.Remove(flag.Key);
        }
    }

    private static FeatureEvaluationDetails Details(
        FeatureFlag flag,
        long revision,
        FeatureVariant variant,
        FeatureEvaluationReason reason,
        IReadOnlyList<string> trace,
        bool usedDraft,
        bool bucketEvaluated = false,
        uint bucket = 0) =>
        new(
            flag.Id,
            flag.Key,
            revision,
            variant.Value,
            variant.Key,
            reason,
            trace,
            bucketEvaluated,
            bucket,
            TargetingContract.BucketCount == 100_000 ? "v1" : "unknown",
            usedDraft);

    private static void ValidateContextScope(
        FeatureScope scope,
        TargetingEvaluationContext context)
    {
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
}
