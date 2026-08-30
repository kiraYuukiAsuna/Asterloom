using Asterloom.Targeting;

namespace Asterloom.Modules.Targeting.Model;

public enum TargetingResourceStatus : short
{
    Active = 1,
    Archived = 2,
}

public sealed record TargetingSegment(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    string Key,
    string DisplayName,
    string Description,
    TargetingRule Rule,
    TargetingResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record TargetingPageRequest(
    int Offset,
    int PageSize,
    string Query,
    bool IncludeArchived);

public sealed record TargetingStorePage<T>(
    IReadOnlyList<T> Items,
    bool HasMore);

public sealed record TargetingListResult<T>(
    IReadOnlyList<T> Items,
    string NextPageToken);

public sealed record TargetingAttributeMetadata(
    string Key,
    string DisplayName,
    TargetingValueKind ValueKind,
    bool BuiltIn,
    bool Required);

public sealed record TargetingOperatorMetadata(
    TargetingOperator Operator,
    string DisplayName,
    IReadOnlyList<TargetingValueKind> SupportedValueKinds,
    int MinimumValues,
    int MaximumValues);

public sealed record TargetingCatalog(
    IReadOnlyList<TargetingAttributeMetadata> Attributes,
    IReadOnlyList<TargetingOperatorMetadata> Operators,
    int MaximumCustomAttributes,
    int MaximumConditions,
    string BucketingVersion,
    uint BucketCount);

public sealed record TargetingBucketPreviewRequest(
    string ResourceType,
    string ResourceKey,
    string Salt,
    IReadOnlyList<TargetingBucketAllocation> Allocations);

public sealed record TargetingSimulationOutcome(
    Guid SegmentId,
    string SegmentKey,
    long SegmentVersion,
    bool Matched,
    string Reason,
    IReadOnlyList<TargetingConditionResult> ConditionTraces,
    bool BucketEvaluated,
    uint Bucket,
    string SelectedVariant,
    string BucketNamespace,
    string BucketingVersion);
