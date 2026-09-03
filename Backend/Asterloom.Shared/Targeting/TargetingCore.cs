using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Asterloom.Targeting;

public enum TargetingValueKind
{
    Text = 1,
    Truth = 2,
    Numeric = 3,
}

public enum TargetingMatchMode
{
    All = 1,
    Any = 2,
}

public enum TargetingOperator
{
    Equals = 1,
    NotEquals = 2,
    OneOf = 3,
    NotOneOf = 4,
    Contains = 5,
    StartsWith = 6,
    EndsWith = 7,
    GreaterThan = 8,
    GreaterThanOrEqual = 9,
    LessThan = 10,
    LessThanOrEqual = 11,
    Exists = 12,
    NotExists = 13,
    SemanticVersionEquals = 14,
    SemanticVersionGreaterThan = 15,
    SemanticVersionLessThan = 16,
}

public enum TargetingConditionReason
{
    Matched = 1,
    NotMatched = 2,
    MissingAttribute = 3,
    TypeMismatch = 4,
    InvalidAttributeValue = 5,
}

public sealed record TargetingValue
{
    public TargetingValue(
        TargetingValueKind kind,
        string? stringValue = null,
        bool? booleanValue = null,
        double? numberValue = null)
    {
        Kind = kind;
        StringValue = stringValue;
        BooleanValue = booleanValue;
        NumberValue = numberValue;
        Validate();
    }

    public TargetingValueKind Kind { get; }

    public string? StringValue { get; }

    public bool? BooleanValue { get; }

    public double? NumberValue { get; }

    public static TargetingValue From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(TargetingValueKind.Text, stringValue: value);
    }

    public static TargetingValue From(bool value) =>
        new(TargetingValueKind.Truth, booleanValue: value);

    public static TargetingValue From(double value) =>
        new(TargetingValueKind.Numeric, numberValue: value);

    public override string ToString() =>
        $"{nameof(TargetingValue)} {{ Kind = {Kind}, Value = [REDACTED] }}";

    private void Validate()
    {
        var populated = (StringValue is null ? 0 : 1)
            + (BooleanValue is null ? 0 : 1)
            + (NumberValue is null ? 0 : 1);
        var valid = Kind switch
        {
            TargetingValueKind.Text => StringValue is not null && populated == 1,
            TargetingValueKind.Truth => BooleanValue is not null && populated == 1,
            TargetingValueKind.Numeric => NumberValue is not null
                && double.IsFinite(NumberValue.Value)
                && populated == 1,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "A targeting value must contain exactly one finite value matching its kind.");
        }
    }
}

public sealed record TargetingCondition(
    string Id,
    string Attribute,
    TargetingValueKind ValueKind,
    TargetingOperator Operator,
    IReadOnlyList<TargetingValue> Values,
    bool CaseSensitive = false)
{
    public override string ToString() =>
        $"{nameof(TargetingCondition)} {{ Id = {Id}, Attribute = {Attribute}, "
        + $"ValueKind = {ValueKind}, Operator = {Operator}, Values = [REDACTED], "
        + $"CaseSensitive = {CaseSensitive} }}";
}

public sealed record TargetingRule(
    TargetingMatchMode MatchMode,
    IReadOnlyList<TargetingCondition> Conditions);

public sealed class TargetingEvaluationContext
{
    public TargetingEvaluationContext(
        string targetingKey,
        Guid applicationId,
        Guid environmentId,
        string? userId = null,
        string? clientVersion = null,
        string? platform = null,
        string? region = null,
        string? language = null,
        IReadOnlyDictionary<string, TargetingValue>? attributes = null)
    {
        TargetingKey = targetingKey ?? throw new ArgumentNullException(nameof(targetingKey));
        ApplicationId = applicationId;
        EnvironmentId = environmentId;
        UserId = userId;
        ClientVersion = clientVersion;
        Platform = platform;
        Region = region;
        Language = language;
        Attributes = new ReadOnlyDictionary<string, TargetingValue>(
            new Dictionary<string, TargetingValue>(
                attributes ?? new Dictionary<string, TargetingValue>(),
                StringComparer.Ordinal));
    }

    public string TargetingKey { get; }

    public string? UserId { get; }

    public Guid ApplicationId { get; }

    public Guid EnvironmentId { get; }

    public string? ClientVersion { get; }

    public string? Platform { get; }

    public string? Region { get; }

    public string? Language { get; }

    public IReadOnlyDictionary<string, TargetingValue> Attributes { get; }

    public override string ToString() =>
        $"{nameof(TargetingEvaluationContext)} {{ TargetingKey = [REDACTED], "
        + $"UserId = [REDACTED], ApplicationId = {ApplicationId:D}, "
        + $"EnvironmentId = {EnvironmentId:D}, Attributes = [REDACTED] }}";
}

public sealed record TargetingConditionResult(
    string ConditionId,
    bool Matched,
    TargetingConditionReason Reason);

public sealed record TargetingRuleResult(
    bool Matched,
    IReadOnlyList<TargetingConditionResult> Conditions);

public sealed record TargetingBucketAllocation(
    string Variant,
    uint Start,
    uint End);

public static partial class TargetingContract
{
    public const uint BucketCount = 100_000;

    private static readonly HashSet<string> BuiltInAttributes = new(
        [
            "targetingKey",
            "userId",
            "applicationId",
            "environmentId",
            "clientVersion",
            "platform",
            "region",
            "language",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> ProhibitedAttributeSegments = new(
        [
            "address",
            "advertisingid",
            "deviceid",
            "email",
            "firstname",
            "fullname",
            "lastname",
            "name",
            "phone",
            "phonenumber",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> KnownAttributes => BuiltInAttributes;

    public static void ValidateContext(TargetingEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireStableValue(context.TargetingKey, nameof(context.TargetingKey), 512);
        if (context.ApplicationId == Guid.Empty)
        {
            throw new ArgumentException("ApplicationId cannot be empty.", nameof(context));
        }

        if (context.EnvironmentId == Guid.Empty)
        {
            throw new ArgumentException("EnvironmentId cannot be empty.", nameof(context));
        }

        ValidateOptionalContextValue(context.UserId, nameof(context.UserId), 200);
        ValidateOptionalContextValue(context.ClientVersion, nameof(context.ClientVersion), 100);
        ValidateOptionalContextValue(context.Platform, nameof(context.Platform), 100);
        ValidateOptionalContextValue(context.Region, nameof(context.Region), 100);
        ValidateOptionalContextValue(context.Language, nameof(context.Language), 100);
        if (context.Attributes.Count > 64)
        {
            throw new ArgumentException(
                "At most 64 custom targeting attributes are accepted.",
                nameof(context));
        }

        foreach (var (key, value) in context.Attributes)
        {
            ValidateCustomAttributeName(key);
            ArgumentNullException.ThrowIfNull(value);
            if (value.Kind == TargetingValueKind.Text && value.StringValue!.Length > 1_000)
            {
                throw new ArgumentException(
                    $"Custom targeting attribute '{key}' exceeds 1000 characters.",
                    nameof(context));
            }
        }
    }

    public static void ValidateRule(TargetingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.MatchMode is not TargetingMatchMode.All and not TargetingMatchMode.Any)
        {
            throw new ArgumentException("Targeting match mode must be ALL or ANY.", nameof(rule));
        }

        ArgumentNullException.ThrowIfNull(rule.Conditions);
        if (rule.Conditions.Count is < 1 or > 50)
        {
            throw new ArgumentException(
                "A targeting rule must contain between 1 and 50 conditions.",
                nameof(rule));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in rule.Conditions)
        {
            ArgumentNullException.ThrowIfNull(condition);
            RequireStableValue(condition.Id, nameof(condition.Id), 100);
            if (!ids.Add(condition.Id))
            {
                throw new ArgumentException(
                    $"Targeting condition ID '{condition.Id}' is duplicated.",
                    nameof(rule));
            }

            ValidateAttributeName(condition.Attribute);
            if (BuiltInAttributes.Contains(condition.Attribute)
                && condition.ValueKind != TargetingValueKind.Text)
            {
                throw new ArgumentException(
                    $"Built-in attribute '{condition.Attribute}' is a string.",
                    nameof(rule));
            }

            ValidateConditionValues(condition);
        }
    }

    public static void ValidateCustomAttributeName(string attribute)
    {
        RequireStableValue(attribute, nameof(attribute), 64);
        if (!AttributePattern().IsMatch(attribute)
            || BuiltInAttributes.Contains(attribute)
            || IsProhibitedAttribute(attribute))
        {
            throw new ArgumentException(
                $"Custom targeting attribute '{attribute}' is invalid, reserved, or PII-like.",
                nameof(attribute));
        }
    }

    public static bool IsProhibitedAttribute(string attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        var segments = attribute
            .Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        var normalized = string.Concat(segments);
        return segments.Any(ProhibitedAttributeSegments.Contains)
            || ProhibitedAttributeSegments.Contains(normalized);
    }

    public static string CreateBucketNamespace(
        string resourceType,
        string resourceKey,
        Guid environmentId)
    {
        var normalizedType = RequireIdentifier(resourceType, nameof(resourceType));
        var normalizedKey = RequireIdentifier(resourceKey, nameof(resourceKey));
        if (environmentId == Guid.Empty)
        {
            throw new ArgumentException("Environment ID cannot be empty.", nameof(environmentId));
        }

        return $"{normalizedType}:{normalizedKey}:{environmentId:D}";
    }

    public static uint ComputeBucket(
        string bucketNamespace,
        string salt,
        string targetingKey)
    {
        RequireStableValue(bucketNamespace, nameof(bucketNamespace), 500);
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length > 500 || HasControlCharactersExceptNull(salt))
        {
            throw new ArgumentException(
                "Salt must not exceed 500 characters or contain control characters.",
                nameof(salt));
        }

        RequireStableValue(targetingKey, nameof(targetingKey), 512);
        var material = string.Concat(
            "v1",
            "\0",
            bucketNamespace,
            "\0",
            salt,
            "\0",
            targetingKey);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return (uint)(BinaryPrimitives.ReadUInt64BigEndian(hash) % BucketCount);
    }

    public static string? SelectBucketAllocation(
        uint bucket,
        IReadOnlyCollection<TargetingBucketAllocation> allocations)
    {
        if (bucket >= BucketCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bucket),
                bucket,
                $"Bucket must be between 0 and {BucketCount - 1}.");
        }

        ArgumentNullException.ThrowIfNull(allocations);
        var ordered = allocations.OrderBy(static item => item.Start).ToArray();
        uint previousEnd = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var allocation = ordered[index];
            RequireStableValue(allocation.Variant, nameof(allocations), 100);
            if (allocation.Start >= allocation.End || allocation.End > BucketCount)
            {
                throw new ArgumentException(
                    "Bucket allocations must use non-empty ranges inside [0, 100000).",
                    nameof(allocations));
            }

            if (index > 0 && allocation.Start < previousEnd)
            {
                throw new ArgumentException(
                    "Bucket allocation ranges cannot overlap.",
                    nameof(allocations));
            }

            previousEnd = allocation.End;
        }

        return ordered.FirstOrDefault(item => bucket >= item.Start && bucket < item.End)
            ?.Variant;
    }

    private static void ValidateAttributeName(string attribute)
    {
        if (BuiltInAttributes.Contains(attribute))
        {
            return;
        }

        ValidateCustomAttributeName(attribute);
    }

    private static void ValidateConditionValues(TargetingCondition condition)
    {
        if (condition.ValueKind is not TargetingValueKind.Text
            and not TargetingValueKind.Truth
            and not TargetingValueKind.Numeric)
        {
            throw new ArgumentException(
                $"Condition '{condition.Id}' has an invalid value kind.");
        }

        ArgumentNullException.ThrowIfNull(condition.Values);
        var expectedCount = condition.Operator switch
        {
            TargetingOperator.Exists or TargetingOperator.NotExists => (Minimum: 0, Maximum: 0),
            TargetingOperator.OneOf or TargetingOperator.NotOneOf => (Minimum: 1, Maximum: 50),
            _ => (Minimum: 1, Maximum: 1),
        };
        if (condition.Values.Count < expectedCount.Minimum
            || condition.Values.Count > expectedCount.Maximum)
        {
            throw new ArgumentException(
                $"Condition '{condition.Id}' has the wrong number of comparison values.");
        }

        if (condition.Values.Any(value => value is null || value.Kind != condition.ValueKind))
        {
            throw new ArgumentException(
                $"Condition '{condition.Id}' contains a value with the wrong type.");
        }

        var validOperator = condition.ValueKind switch
        {
            TargetingValueKind.Text => condition.Operator is
                TargetingOperator.Equals
                or TargetingOperator.NotEquals
                or TargetingOperator.OneOf
                or TargetingOperator.NotOneOf
                or TargetingOperator.Contains
                or TargetingOperator.StartsWith
                or TargetingOperator.EndsWith
                or TargetingOperator.Exists
                or TargetingOperator.NotExists
                or TargetingOperator.SemanticVersionEquals
                or TargetingOperator.SemanticVersionGreaterThan
                or TargetingOperator.SemanticVersionLessThan,
            TargetingValueKind.Truth => condition.Operator is
                TargetingOperator.Equals
                or TargetingOperator.NotEquals
                or TargetingOperator.OneOf
                or TargetingOperator.NotOneOf
                or TargetingOperator.Exists
                or TargetingOperator.NotExists,
            TargetingValueKind.Numeric => condition.Operator is
                TargetingOperator.Equals
                or TargetingOperator.NotEquals
                or TargetingOperator.OneOf
                or TargetingOperator.NotOneOf
                or TargetingOperator.GreaterThan
                or TargetingOperator.GreaterThanOrEqual
                or TargetingOperator.LessThan
                or TargetingOperator.LessThanOrEqual
                or TargetingOperator.Exists
                or TargetingOperator.NotExists,
            _ => false,
        };
        if (!validOperator)
        {
            throw new ArgumentException(
                $"Operator '{condition.Operator}' is not valid for {condition.ValueKind} values.");
        }

        if (condition.Values.Any(value =>
                value.Kind == TargetingValueKind.Text
                && value.StringValue!.Length > 1_000))
        {
            throw new ArgumentException(
                $"Condition '{condition.Id}' contains a string longer than 1000 characters.");
        }

        if (condition.Operator is TargetingOperator.SemanticVersionEquals
            or TargetingOperator.SemanticVersionGreaterThan
            or TargetingOperator.SemanticVersionLessThan
            && !SemanticVersion.TryParse(condition.Values[0].StringValue!, out _))
        {
            throw new ArgumentException(
                $"Condition '{condition.Id}' contains an invalid semantic version.");
        }
    }

    private static string RequireIdentifier(string value, string parameterName)
    {
        RequireStableValue(value, parameterName, 100);
        if (!IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException(
                "Use lowercase letters, numbers, periods, underscores, or hyphens; start and end with a letter or number.",
                parameterName);
        }

        return value;
    }

    private static void RequireStableValue(string value, string parameterName, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || HasControlCharactersExceptNull(value))
        {
            throw new ArgumentException(
                $"A stable value between 1 and {maximumLength} characters without surrounding whitespace or control characters is required.",
                parameterName);
        }
    }

    private static void ValidateOptionalContextValue(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (value is null)
        {
            return;
        }

        RequireStableValue(value, parameterName, maximumLength);
    }

    private static bool HasControlCharactersExceptNull(string value) =>
        value.Any(character => char.IsControl(character));

    [GeneratedRegex(
        "^[a-z][a-zA-Z0-9_.-]{0,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex AttributePattern();

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}

public static class TargetingEvaluator
{
    public static TargetingRuleResult Evaluate(
        TargetingRule rule,
        TargetingEvaluationContext context)
    {
        TargetingContract.ValidateRule(rule);
        TargetingContract.ValidateContext(context);
        return EvaluateCore(
            rule,
            (string attribute, out TargetingValue? value) =>
                TryGetValue(context, attribute, out value));
    }

    public static TargetingRuleResult Evaluate(
        TargetingRule rule,
        IReadOnlyDictionary<string, TargetingValue> attributes)
    {
        TargetingContract.ValidateRule(rule);
        ArgumentNullException.ThrowIfNull(attributes);
        if (attributes.Count > 128)
        {
            throw new ArgumentException(
                "At most 128 authorization attributes are accepted.",
                nameof(attributes));
        }

        foreach (var (key, value) in attributes)
        {
            TargetingContract.ValidateCustomAttributeName(key);
            ArgumentNullException.ThrowIfNull(value);
            if (value.Kind == TargetingValueKind.Text && value.StringValue!.Length > 1_000)
            {
                throw new ArgumentException(
                    $"Authorization attribute '{key}' exceeds 1000 characters.",
                    nameof(attributes));
            }
        }

        return EvaluateCore(
            rule,
            (string attribute, out TargetingValue? value) =>
                attributes.TryGetValue(attribute, out value));
    }

    private static TargetingRuleResult EvaluateCore(
        TargetingRule rule,
        TryResolveAttribute resolveAttribute)
    {
        var results = new List<TargetingConditionResult>(rule.Conditions.Count);
        foreach (var condition in rule.Conditions)
        {
            var result = EvaluateCondition(condition, resolveAttribute);
            results.Add(result);
            if (rule.MatchMode == TargetingMatchMode.All && !result.Matched)
            {
                return new(false, results);
            }

            if (rule.MatchMode == TargetingMatchMode.Any && result.Matched)
            {
                return new(true, results);
            }
        }

        return new(rule.MatchMode == TargetingMatchMode.All, results);
    }

    private static TargetingConditionResult EvaluateCondition(
        TargetingCondition condition,
        TryResolveAttribute resolveAttribute)
    {
        var exists = resolveAttribute(condition.Attribute, out var actual);
        if (condition.Operator == TargetingOperator.Exists)
        {
            return Result(
                condition.Id,
                exists,
                exists ? TargetingConditionReason.Matched : TargetingConditionReason.MissingAttribute);
        }

        if (condition.Operator == TargetingOperator.NotExists)
        {
            return Result(
                condition.Id,
                !exists,
                exists ? TargetingConditionReason.NotMatched : TargetingConditionReason.MissingAttribute);
        }

        if (!exists)
        {
            return Result(condition.Id, false, TargetingConditionReason.MissingAttribute);
        }

        if (actual!.Kind != condition.ValueKind)
        {
            return Result(condition.Id, false, TargetingConditionReason.TypeMismatch);
        }

        var matched = condition.ValueKind switch
        {
            TargetingValueKind.Text => EvaluateString(condition, actual.StringValue!),
            TargetingValueKind.Truth => EvaluateBoolean(condition, actual.BooleanValue!.Value),
            TargetingValueKind.Numeric => EvaluateNumber(condition, actual.NumberValue!.Value),
            _ => false,
        };
        return matched is null
            ? Result(condition.Id, false, TargetingConditionReason.InvalidAttributeValue)
            : Result(
                condition.Id,
                matched.Value,
                matched.Value
                    ? TargetingConditionReason.Matched
                    : TargetingConditionReason.NotMatched);
    }

    private static bool? EvaluateString(TargetingCondition condition, string actual)
    {
        var comparison = condition.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var values = condition.Values.Select(static value => value.StringValue!).ToArray();
        return condition.Operator switch
        {
            TargetingOperator.Equals => string.Equals(actual, values[0], comparison),
            TargetingOperator.NotEquals => !string.Equals(actual, values[0], comparison),
            TargetingOperator.OneOf => values.Contains(actual, StringComparerFrom(comparison)),
            TargetingOperator.NotOneOf => !values.Contains(actual, StringComparerFrom(comparison)),
            TargetingOperator.Contains => actual.Contains(values[0], comparison),
            TargetingOperator.StartsWith => actual.StartsWith(values[0], comparison),
            TargetingOperator.EndsWith => actual.EndsWith(values[0], comparison),
            TargetingOperator.SemanticVersionEquals => CompareSemanticVersions(actual, values[0], 0),
            TargetingOperator.SemanticVersionGreaterThan => CompareSemanticVersions(actual, values[0], 1),
            TargetingOperator.SemanticVersionLessThan => CompareSemanticVersions(actual, values[0], -1),
            _ => false,
        };
    }

    private static bool EvaluateBoolean(TargetingCondition condition, bool actual)
    {
        var values = condition.Values.Select(static value => value.BooleanValue!.Value).ToArray();
        return condition.Operator switch
        {
            TargetingOperator.Equals => actual == values[0],
            TargetingOperator.NotEquals => actual != values[0],
            TargetingOperator.OneOf => values.Contains(actual),
            TargetingOperator.NotOneOf => !values.Contains(actual),
            _ => false,
        };
    }

    private static bool EvaluateNumber(TargetingCondition condition, double actual)
    {
        var values = condition.Values.Select(static value => value.NumberValue!.Value).ToArray();
        return condition.Operator switch
        {
            TargetingOperator.Equals => actual.Equals(values[0]),
            TargetingOperator.NotEquals => !actual.Equals(values[0]),
            TargetingOperator.OneOf => values.Contains(actual),
            TargetingOperator.NotOneOf => !values.Contains(actual),
            TargetingOperator.GreaterThan => actual > values[0],
            TargetingOperator.GreaterThanOrEqual => actual >= values[0],
            TargetingOperator.LessThan => actual < values[0],
            TargetingOperator.LessThanOrEqual => actual <= values[0],
            _ => false,
        };
    }

    private static bool? CompareSemanticVersions(string actual, string expected, int expectedSign)
    {
        if (!SemanticVersion.TryParse(actual, out var actualVersion)
            || !SemanticVersion.TryParse(expected, out var expectedVersion))
        {
            return null;
        }

        return Math.Sign(actualVersion.CompareTo(expectedVersion)) == expectedSign;
    }

    private static StringComparer StringComparerFrom(StringComparison comparison) =>
        comparison == StringComparison.Ordinal
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

    private static bool TryGetValue(
        TargetingEvaluationContext context,
        string attribute,
        out TargetingValue? value)
    {
        value = attribute switch
        {
            "targetingKey" => TargetingValue.From(context.TargetingKey),
            "userId" when context.UserId is not null => TargetingValue.From(context.UserId),
            "applicationId" => TargetingValue.From(context.ApplicationId.ToString("D")),
            "environmentId" => TargetingValue.From(context.EnvironmentId.ToString("D")),
            "clientVersion" when context.ClientVersion is not null =>
                TargetingValue.From(context.ClientVersion),
            "platform" when context.Platform is not null => TargetingValue.From(context.Platform),
            "region" when context.Region is not null => TargetingValue.From(context.Region),
            "language" when context.Language is not null => TargetingValue.From(context.Language),
            _ => null,
        };
        return value is not null || context.Attributes.TryGetValue(attribute, out value);
    }

    private static TargetingConditionResult Result(
        string conditionId,
        bool matched,
        TargetingConditionReason reason) => new(conditionId, matched, reason);

    private delegate bool TryResolveAttribute(
        string attribute,
        out TargetingValue? value);
}

internal sealed partial class SemanticVersion : IComparable<SemanticVersion>
{
    private SemanticVersion(
        string major,
        string minor,
        string patch,
        IReadOnlyList<string> preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    private string Major { get; }

    private string Minor { get; }

    private string Patch { get; }

    private IReadOnlyList<string> PreRelease { get; }

    public static bool TryParse(string value, out SemanticVersion version)
    {
        version = null!;
        var match = VersionPattern().Match(value);
        if (!match.Success)
        {
            return false;
        }

        var preRelease = match.Groups["pre"].Success
            ? match.Groups["pre"].Value.Split('.')
            : [];
        if (preRelease.Any(identifier =>
                IsNumeric(identifier)
                && identifier.Length > 1
                && identifier[0] == '0'))
        {
            return false;
        }

        version = new(
            match.Groups["major"].Value,
            match.Groups["minor"].Value,
            match.Groups["patch"].Value,
            preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var comparison = CompareNumeric(Major, other.Major);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareNumeric(Minor, other.Minor);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareNumeric(Patch, other.Patch);
        if (comparison != 0)
        {
            return comparison;
        }

        if (PreRelease.Count == 0 || other.PreRelease.Count == 0)
        {
            return PreRelease.Count == other.PreRelease.Count
                ? 0
                : PreRelease.Count == 0 ? 1 : -1;
        }

        for (var index = 0; index < Math.Min(PreRelease.Count, other.PreRelease.Count); index++)
        {
            var left = PreRelease[index];
            var right = other.PreRelease[index];
            var leftNumeric = IsNumeric(left);
            var rightNumeric = IsNumeric(right);
            comparison = (leftNumeric, rightNumeric) switch
            {
                (true, true) => CompareNumeric(left, right),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.Compare(left, right, StringComparison.Ordinal),
            };
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    private static bool IsNumeric(string value) =>
        value.All(static character => character is >= '0' and <= '9');

    private static int CompareNumeric(string left, string right)
    {
        var length = left.Length.CompareTo(right.Length);
        return length != 0 ? length : string.Compare(left, right, StringComparison.Ordinal);
    }

    [GeneratedRegex(
        "^(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
