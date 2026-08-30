using System.Text.Json;
using Asterloom.Targeting;

namespace Asterloom.Modules.Config.Model;

public enum ConfigValueKind : short
{
    Truth = 1,
    WholeNumber = 2,
    DecimalNumber = 3,
    Text = 4,
    Structure = 5,
}

public enum ConfigVisibility : short
{
    Client = 1,
    Server = 2,
}

public enum ConfigResourceStatus : short
{
    Active = 1,
    Archived = 2,
}

public enum ConfigValidationSeverity : short
{
    Error = 1,
    Warning = 2,
}

public enum ConfigEvaluationReason
{
    TargetingMatch = 1,
    Default = 2,
}

public sealed record ConfigValue
{
    public ConfigValue(
        ConfigValueKind kind,
        bool? booleanValue = null,
        long? integerValue = null,
        double? doubleValue = null,
        string? stringValue = null,
        string? jsonValue = null)
    {
        Kind = kind;
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        DoubleValue = doubleValue;
        StringValue = stringValue;
        JsonValue = jsonValue;
        Validate();
    }

    public ConfigValueKind Kind { get; }

    public bool? BooleanValue { get; }

    public long? IntegerValue { get; }

    public double? DoubleValue { get; }

    public string? StringValue { get; }

    public string? JsonValue { get; }

    public static ConfigValue From(bool value) =>
        new(ConfigValueKind.Truth, booleanValue: value);

    public static ConfigValue From(long value) =>
        new(ConfigValueKind.WholeNumber, integerValue: value);

    public static ConfigValue From(double value) =>
        new(ConfigValueKind.DecimalNumber, doubleValue: value);

    public static ConfigValue From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(ConfigValueKind.Text, stringValue: value);
    }

    public static ConfigValue FromJson(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(ConfigValueKind.Structure, jsonValue: value);
    }

    public string ToCanonicalJson() => Kind switch
    {
        ConfigValueKind.Truth => BooleanValue!.Value ? "true" : "false",
        ConfigValueKind.WholeNumber => IntegerValue!.Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture),
        ConfigValueKind.DecimalNumber => DoubleValue!.Value.ToString(
            "R",
            System.Globalization.CultureInfo.InvariantCulture),
        ConfigValueKind.Text => JsonSerializer.Serialize(StringValue),
        ConfigValueKind.Structure => CanonicalizeJson(JsonValue!),
        _ => throw new InvalidOperationException("The configuration value kind is invalid."),
    };

    public override string ToString() =>
        $"{nameof(ConfigValue)} {{ Kind = {Kind}, Value = [REDACTED] }}";

    private void Validate()
    {
        var populated = (BooleanValue.HasValue ? 1 : 0)
            + (IntegerValue.HasValue ? 1 : 0)
            + (DoubleValue.HasValue ? 1 : 0)
            + (StringValue is null ? 0 : 1)
            + (JsonValue is null ? 0 : 1);
        var valid = Kind switch
        {
            ConfigValueKind.Truth => BooleanValue.HasValue && populated == 1,
            ConfigValueKind.WholeNumber => IntegerValue.HasValue && populated == 1,
            ConfigValueKind.DecimalNumber => DoubleValue.HasValue
                && double.IsFinite(DoubleValue.Value)
                && populated == 1,
            ConfigValueKind.Text => StringValue is not null
                && StringValue.Length <= 100_000
                && populated == 1,
            ConfigValueKind.Structure => JsonValue is not null
                && JsonValue.Length <= 250_000
                && populated == 1
                && IsJsonObject(JsonValue),
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "A configuration value must contain exactly one valid value matching its kind.");
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

    private static string CanonicalizeJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return JsonSerializer.Serialize(document.RootElement);
    }
}

public sealed record ConfigTargetingRule(
    string Id,
    Guid SegmentId,
    ConfigValue Value);

public sealed record ConfigDefinition(
    string SchemaJson,
    ConfigValue DefaultValue,
    IReadOnlyList<ConfigTargetingRule> TargetingRules);

public sealed record ConfigEntry(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    string Key,
    string DisplayName,
    string Description,
    ConfigValueKind ValueKind,
    ConfigVisibility Visibility,
    ConfigResourceStatus Status,
    ConfigDefinition DraftDefinition,
    long DraftRevision,
    ConfigDefinition? PublishedDefinition,
    long? PublishedRevision,
    long? PublishedSnapshotVersion,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset? PublishedAt);

public sealed record ConfigRevision(
    Guid Id,
    Guid EntryId,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    long Revision,
    ConfigDefinition Definition,
    long? SourceRevision,
    long SnapshotVersion,
    DateTimeOffset PublishedAt);

public sealed record ConfigSnapshotItem(
    Guid EntryId,
    string Key,
    ConfigValueKind ValueKind,
    ConfigVisibility Visibility,
    long Revision,
    ConfigDefinition Definition,
    IReadOnlyList<ConfigSnapshotTargetingRule> TargetingRules);

public sealed record ConfigSnapshotTargetingRule(
    string Id,
    Guid SegmentId,
    long SegmentVersion,
    ConfigValue Value,
    TargetingRule Rule);

public sealed record ConfigSnapshot(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId,
    long Version,
    IReadOnlyList<ConfigSnapshotItem> Items,
    DateTimeOffset CreatedAt);

public sealed record ConfigValidationIssue(
    ConfigValidationSeverity Severity,
    string Code,
    string Path,
    string Message);

public sealed record ConfigValidationResult(
    bool Valid,
    IReadOnlyList<ConfigValidationIssue> Issues,
    string DefinitionHash);

public sealed record ConfigDiff(
    bool Changed,
    string PublishedJson,
    string DraftJson,
    IReadOnlyList<string> ChangedPaths);

public sealed record ConfigEffectiveValue(
    Guid EntryId,
    string Key,
    ConfigValueKind ValueKind,
    ConfigValue Value,
    long Revision,
    ConfigEvaluationReason Reason,
    string? TargetingRuleId);

public sealed record ConfigSnapshotResult(
    long SnapshotVersion,
    string ETag,
    bool NotModified,
    IReadOnlyList<ConfigEffectiveValue> Values,
    DateTimeOffset GeneratedAt);

public sealed record ConfigUpdateStatus(
    bool Changed,
    long CurrentSnapshotVersion,
    string ETag,
    DateTimeOffset CheckedAt);

public sealed record ConfigScope(
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId);

public sealed record ConfigPageRequest(
    int Offset,
    int PageSize,
    string Query,
    bool IncludeArchived);

public sealed record ConfigStorePage<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed record ConfigListResult<T>(IReadOnlyList<T> Items, string NextPageToken);

public sealed record ConfigPublishedEvent(
    Guid EntryId,
    string Key,
    long Revision,
    long SnapshotVersion,
    long? SourceRevision,
    string DefinitionHash);
