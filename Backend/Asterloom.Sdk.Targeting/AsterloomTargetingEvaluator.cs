using Asterloom.Targeting;

namespace Asterloom.Sdk.Targeting;

public static class AsterloomTargetingEvaluator
{
    public static TargetingRuleResult Evaluate(
        TargetingRule rule,
        TargetingEvaluationContext context) =>
        TargetingEvaluator.Evaluate(rule, context);

    public static uint ComputeBucket(
        string bucketNamespace,
        string salt,
        string targetingKey) =>
        TargetingContract.ComputeBucket(bucketNamespace, salt, targetingKey);

    public static uint ComputeBucket(
        string resourceType,
        string resourceKey,
        Guid environmentId,
        string salt,
        string targetingKey)
    {
        var bucketNamespace = TargetingContract.CreateBucketNamespace(
            resourceType,
            resourceKey,
            environmentId);
        return TargetingContract.ComputeBucket(bucketNamespace, salt, targetingKey);
    }

    public static string? SelectBucketAllocation(
        uint bucket,
        IReadOnlyCollection<TargetingBucketAllocation> allocations) =>
        TargetingContract.SelectBucketAllocation(bucket, allocations);
}
