using Asterloom.Targeting;

namespace Asterloom.Sdk.Targeting;

public enum AsterloomTargetingResourceStatus
{
    Active = 1,
    Archived = 2,
}

public sealed record AsterloomTargetingScope(
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId);

public sealed record AsterloomTargetingPage<T>(
    IReadOnlyList<T> Items,
    string? NextPageToken);

public sealed record AsterloomTargetingSegment(
    Guid Id,
    AsterloomTargetingScope Scope,
    string Key,
    string DisplayName,
    string Description,
    TargetingRule Rule,
    AsterloomTargetingResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record AsterloomTargetingSegmentRegistration(
    string Key,
    string DisplayName,
    string? Description,
    TargetingRule Rule);

public sealed record AsterloomTargetingSegmentUpdate(
    string DisplayName,
    string? Description,
    TargetingRule Rule);

public sealed record AsterloomTargetingAttributeDefinition(
    string Key,
    string DisplayName,
    TargetingValueKind ValueKind,
    bool BuiltIn,
    bool Required);

public sealed record AsterloomTargetingOperatorDefinition(
    TargetingOperator Operator,
    string DisplayName,
    IReadOnlyList<TargetingValueKind> SupportedValueKinds,
    int MinimumValues,
    int MaximumValues);

public sealed record AsterloomTargetingCatalog(
    IReadOnlyList<AsterloomTargetingAttributeDefinition> Attributes,
    IReadOnlyList<AsterloomTargetingOperatorDefinition> Operators,
    int MaximumCustomAttributes,
    int MaximumConditions,
    string BucketingVersion,
    uint BucketCount);

public sealed record AsterloomTargetingBucketPreview(
    string ResourceType,
    string ResourceKey,
    string Salt,
    IReadOnlyList<TargetingBucketAllocation> Allocations)
{
    public override string ToString() =>
        $"{nameof(AsterloomTargetingBucketPreview)} {{ ResourceType = {ResourceType}, "
        + $"ResourceKey = {ResourceKey}, Salt = [REDACTED], "
        + $"Allocations = {Allocations.Count} }}";
}

public sealed record AsterloomTargetingSimulationResult(
    Guid SegmentId,
    string SegmentKey,
    long SegmentVersion,
    bool Matched,
    string Reason,
    IReadOnlyList<TargetingConditionResult> ConditionTraces,
    bool BucketEvaluated,
    uint Bucket,
    string? SelectedVariant,
    string? BucketNamespace,
    string BucketingVersion);
