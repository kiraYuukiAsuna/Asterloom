using System.Text.Json;

namespace Asterloom.Sdk.Feature;

public enum AsterloomFeatureValueKind
{
    Truth = 1,
    Text = 2,
    WholeNumber = 3,
    DecimalNumber = 4,
    Structure = 5,
}

public enum AsterloomFeatureResourceStatus
{
    Active = 1,
    Archived = 2,
}

public enum AsterloomFeatureValidationSeverity
{
    Error = 1,
    Warning = 2,
}

public enum AsterloomFeatureEvaluationReason
{
    Disabled = 1,
    TargetingMatch = 2,
    Split = 3,
    Default = 4,
    PrerequisiteFailed = 5,
}

public sealed record AsterloomFeatureValue
{
    public AsterloomFeatureValue(
        AsterloomFeatureValueKind kind,
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

    public AsterloomFeatureValueKind Kind { get; }

    public bool? BooleanValue { get; }

    public string? StringValue { get; }

    public long? IntegerValue { get; }

    public double? DoubleValue { get; }

    public string? ObjectJson { get; }

    public static AsterloomFeatureValue From(bool value) =>
        new(AsterloomFeatureValueKind.Truth, booleanValue: value);

    public static AsterloomFeatureValue From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(AsterloomFeatureValueKind.Text, stringValue: value);
    }

    public static AsterloomFeatureValue From(long value) =>
        new(AsterloomFeatureValueKind.WholeNumber, integerValue: value);

    public static AsterloomFeatureValue From(double value) =>
        new(AsterloomFeatureValueKind.DecimalNumber, doubleValue: value);

    public static AsterloomFeatureValue FromJson(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(AsterloomFeatureValueKind.Structure, objectJson: value);
    }

    public override string ToString() =>
        $"{nameof(AsterloomFeatureValue)} {{ Kind = {Kind}, Value = [REDACTED] }}";

    private void Validate()
    {
        var populated = (BooleanValue.HasValue ? 1 : 0)
            + (StringValue is null ? 0 : 1)
            + (IntegerValue.HasValue ? 1 : 0)
            + (DoubleValue.HasValue ? 1 : 0)
            + (ObjectJson is null ? 0 : 1);
        var valid = Kind switch
        {
            AsterloomFeatureValueKind.Truth => BooleanValue.HasValue && populated == 1,
            AsterloomFeatureValueKind.Text => StringValue is not null
                && StringValue.Length <= 10_000
                && populated == 1,
            AsterloomFeatureValueKind.WholeNumber => IntegerValue.HasValue && populated == 1,
            AsterloomFeatureValueKind.DecimalNumber => DoubleValue.HasValue
                && double.IsFinite(DoubleValue.Value)
                && populated == 1,
            AsterloomFeatureValueKind.Structure => ObjectJson is not null
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

public sealed record AsterloomFeatureVariant(
    string Key,
    string DisplayName,
    AsterloomFeatureValue Value);

public sealed record AsterloomFeaturePrerequisite(
    string FlagKey,
    string ExpectedVariantKey);

public sealed record AsterloomFeatureTargetingRule(
    string Id,
    Guid SegmentId,
    string VariantKey);

public sealed record AsterloomFeatureAllocation(
    string VariantKey,
    uint Start,
    uint End);

public sealed record AsterloomFeatureDefinition(
    bool Enabled,
    string DefaultVariantKey,
    IReadOnlyList<AsterloomFeatureVariant> Variants,
    IReadOnlyList<AsterloomFeaturePrerequisite> Prerequisites,
    IReadOnlyList<AsterloomFeatureTargetingRule> TargetingRules,
    IReadOnlyList<AsterloomFeatureAllocation> Allocations,
    string BucketingSalt)
{
    public override string ToString() =>
        $"{nameof(AsterloomFeatureDefinition)} {{ Enabled = {Enabled}, "
        + $"DefaultVariantKey = {DefaultVariantKey}, Variants = {Variants.Count}, "
        + $"Prerequisites = {Prerequisites.Count}, TargetingRules = {TargetingRules.Count}, "
        + $"Allocations = {Allocations.Count}, BucketingSalt = [REDACTED] }}";
}

public sealed record AsterloomFeatureFlag(
    Guid Id,
    AsterloomFeatureScope Scope,
    string Key,
    string DisplayName,
    string Description,
    AsterloomFeatureValueKind ValueKind,
    AsterloomFeatureResourceStatus Status,
    AsterloomFeatureDefinition DraftDefinition,
    long DraftRevision,
    AsterloomFeatureDefinition? PublishedDefinition,
    long? PublishedRevision,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset? PublishedAt);

public sealed record AsterloomFeatureRevision(
    Guid Id,
    Guid FlagId,
    long Revision,
    AsterloomFeatureDefinition Definition,
    long? SourceRevision,
    DateTimeOffset PublishedAt);

public sealed record AsterloomFeatureValidationIssue(
    AsterloomFeatureValidationSeverity Severity,
    string Code,
    string Path,
    string Message);

public sealed record AsterloomFeatureValidationResult(
    bool Valid,
    IReadOnlyList<AsterloomFeatureValidationIssue> Issues,
    string DefinitionHash);

public sealed record AsterloomFeatureEvaluationDetails(
    Guid FlagId,
    string FlagKey,
    long Revision,
    AsterloomFeatureValue Value,
    string VariantKey,
    AsterloomFeatureEvaluationReason Reason,
    IReadOnlyList<string> Trace,
    bool BucketEvaluated,
    uint Bucket,
    string BucketingVersion,
    bool UsedDraft);

public sealed record AsterloomFeaturePage<T>(
    IReadOnlyList<T> Items,
    string? NextPageToken);

public sealed record AsterloomFeatureRegistration(
    string Key,
    string DisplayName,
    string? Description,
    AsterloomFeatureValueKind ValueKind,
    AsterloomFeatureDefinition Definition);

public sealed record AsterloomFeatureDraftUpdate(
    string DisplayName,
    string? Description,
    AsterloomFeatureDefinition Definition);

public sealed record AsterloomFeatureScope(
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId);

public sealed class AsterloomFeatureProviderOptions
{
    public required AsterloomFeatureScope Scope { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan LastKnownGoodDuration { get; init; } = TimeSpan.FromHours(24);

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Scope);
        if (Scope.TenantId == Guid.Empty
            || Scope.ApplicationId == Guid.Empty
            || Scope.EnvironmentId == Guid.Empty)
        {
            throw new ArgumentException("Feature scope identifiers cannot be empty.", nameof(Scope));
        }

        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequestTimeout),
                "Request timeout must be between zero and two minutes.");
        }

        if (CacheDuration < TimeSpan.Zero || CacheDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(CacheDuration),
                "Cache duration must be between zero and one hour.");
        }

        if (LastKnownGoodDuration < CacheDuration
            || LastKnownGoodDuration > TimeSpan.FromDays(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(LastKnownGoodDuration),
                "Last-known-good duration must include the cache duration and not exceed 30 days.");
        }
    }
}
