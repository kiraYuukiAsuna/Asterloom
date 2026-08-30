using System.Text.Json;
using Asterloom.Targeting;

namespace Asterloom.Modules.Feature.Model;

public enum FeatureValueKind : short
{
    Truth = 1,
    Text = 2,
    WholeNumber = 3,
    DecimalNumber = 4,
    Structure = 5,
}

public enum FeatureResourceStatus : short
{
    Active = 1,
    Archived = 2,
}

public enum FeatureValidationSeverity : short
{
    Error = 1,
    Warning = 2,
}

public enum FeatureEvaluationReason
{
    Disabled = 1,
    TargetingMatch = 2,
    Split = 3,
    Default = 4,
    PrerequisiteFailed = 5,
}

public sealed record FeatureValue
{
    public FeatureValue(
        FeatureValueKind kind,
        bool? booleanValue = null,
        string? stringValue = null,
        long? integerValue = null,
        double? doubleValue = null,
        string? objectJson = null)
    {
        Kind = kind;
        BooleanValue = booleanValue;
        StringValue = stringValue;
        IntegerValue = integerValue;
        DoubleValue = doubleValue;
        ObjectJson = objectJson;
        Validate();
    }

    public FeatureValueKind Kind { get; }

    public bool? BooleanValue { get; }

    public string? StringValue { get; }

    public long? IntegerValue { get; }

    public double? DoubleValue { get; }

    public string? ObjectJson { get; }

    public static FeatureValue From(bool value) =>
        new(FeatureValueKind.Truth, booleanValue: value);

    public static FeatureValue From(string value) =>
        new(FeatureValueKind.Text, stringValue: value);

    public static FeatureValue From(long value) =>
        new(FeatureValueKind.WholeNumber, integerValue: value);

    public static FeatureValue From(double value) =>
        new(FeatureValueKind.DecimalNumber, doubleValue: value);

    public static FeatureValue FromJson(string value) =>
        new(FeatureValueKind.Structure, objectJson: value);

    public override string ToString() =>
        $"{nameof(FeatureValue)} {{ Kind = {Kind}, Value = [REDACTED] }}";

    private void Validate()
    {
        var populated = (BooleanValue.HasValue ? 1 : 0)
            + (StringValue is null ? 0 : 1)
            + (IntegerValue.HasValue ? 1 : 0)
            + (DoubleValue.HasValue ? 1 : 0)
            + (ObjectJson is null ? 0 : 1);
        var valid = Kind switch
        {
            FeatureValueKind.Truth => BooleanValue.HasValue && populated == 1,
            FeatureValueKind.Text => StringValue is not null
                && StringValue.Length <= 10_000
                && populated == 1,
            FeatureValueKind.WholeNumber => IntegerValue.HasValue && populated == 1,
            FeatureValueKind.DecimalNumber => DoubleValue.HasValue
                && double.IsFinite(DoubleValue.Value)
                && populated == 1,
            FeatureValueKind.Structure => ObjectJson is not null
                && ObjectJson.Length <= 100_000
                && populated == 1
                && IsJsonObject(ObjectJson),
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "A feature value must contain exactly one valid value matching its kind.");
        }
    }

    private static bool IsJsonObject(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record FeatureVariant(
    string Key,
    string DisplayName,
    FeatureValue Value);

public sealed record FeaturePrerequisite(
    string FlagKey,
    string ExpectedVariantKey);

public sealed record FeatureTargetingRule(
    string Id,
    Guid SegmentId,
    string VariantKey);

public sealed record FeatureAllocation(
    string VariantKey,
    uint Start,
    uint End);

public sealed record FeatureDefinition(
    bool Enabled,
    string DefaultVariantKey,
    IReadOnlyList<FeatureVariant> Variants,
    IReadOnlyList<FeaturePrerequisite> Prerequisites,
    IReadOnlyList<FeatureTargetingRule> TargetingRules,
    IReadOnlyList<FeatureAllocation> Allocations,
    string BucketingSalt);

public sealed record FeatureFlag(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    string Key,
    string DisplayName,
    string Description,
    FeatureValueKind ValueKind,
    FeatureResourceStatus Status,
    FeatureDefinition DraftDefinition,
    long DraftRevision,
    FeatureDefinition? PublishedDefinition,
    long? PublishedRevision,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset? PublishedAt);

public sealed record FeatureRevision(
    Guid Id,
    Guid FlagId,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    long Revision,
    FeatureDefinition Definition,
    long? SourceRevision,
    DateTimeOffset PublishedAt);

public sealed record FeatureValidationIssue(
    FeatureValidationSeverity Severity,
    string Code,
    string Path,
    string Message);

public sealed record FeatureValidationResult(
    bool Valid,
    IReadOnlyList<FeatureValidationIssue> Issues,
    string DefinitionHash);

public sealed record FeatureEvaluationDetails(
    Guid FlagId,
    string FlagKey,
    long Revision,
    FeatureValue Value,
    string VariantKey,
    FeatureEvaluationReason Reason,
    IReadOnlyList<string> Trace,
    bool BucketEvaluated,
    uint Bucket,
    string BucketingVersion,
    bool UsedDraft);

public sealed record FeatureScope(
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId);

public sealed record FeaturePageRequest(
    int Offset,
    int PageSize,
    string Query,
    bool IncludeArchived);

public sealed record FeatureStorePage<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed record FeatureListResult<T>(IReadOnlyList<T> Items, string NextPageToken);

public sealed record FeaturePublishedEvent(
    Guid FlagId,
    string FlagKey,
    long Revision,
    long? SourceRevision,
    string DefinitionHash);

public sealed record FeatureEvaluationRequest(
    FeatureScope Scope,
    string FlagKey,
    FeatureValueKind? ExpectedKind,
    TargetingEvaluationContext Context,
    bool UseDraft = false,
    Guid? FlagId = null);
