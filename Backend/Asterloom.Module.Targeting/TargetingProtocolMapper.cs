using Asterloom.Modules.Errors;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Targeting;
using Google.Protobuf.WellKnownTypes;
using ProtocolAllocation = Asterloom.Protocol.Targeting.V1.BucketAllocation;
using ProtocolAttribute = Asterloom.Protocol.Targeting.V1.TargetingAttributeDefinition;
using ProtocolCondition = Asterloom.Protocol.Targeting.V1.TargetingCondition;
using ProtocolConditionReason = Asterloom.Protocol.Targeting.V1.TargetingConditionReason;
using ProtocolContext = Asterloom.Protocol.Targeting.V1.EvaluationContext;
using ProtocolMatchMode = Asterloom.Protocol.Targeting.V1.TargetingMatchMode;
using ProtocolOperator = Asterloom.Protocol.Targeting.V1.TargetingOperator;
using ProtocolOperatorDefinition = Asterloom.Protocol.Targeting.V1.TargetingOperatorDefinition;
using ProtocolRule = Asterloom.Protocol.Targeting.V1.TargetingRule;
using ProtocolSegment = Asterloom.Protocol.Targeting.V1.Segment;
using ProtocolSimulationResult = Asterloom.Protocol.Targeting.V1.TargetingSimulationResult;
using ProtocolStatus = Asterloom.Protocol.Targeting.V1.TargetingResourceStatus;
using ProtocolValue = Asterloom.Protocol.Targeting.V1.TargetingValue;
using ProtocolValueKind = Asterloom.Protocol.Targeting.V1.TargetingValueKind;

namespace Asterloom.Modules.Targeting;

public static class TargetingProtocolMapper
{
    public static ProtocolSegment ToProtocol(this TargetingSegment segment) => new()
    {
        Id = segment.Id.ToString("D"),
        TenantId = segment.TenantId.ToString("D"),
        ApplicationId = segment.ApplicationId.ToString("D"),
        EnvironmentId = segment.EnvironmentId.ToString("D"),
        Key = segment.Key,
        DisplayName = segment.DisplayName,
        Description = segment.Description,
        Rule = segment.Rule.ToProtocol(),
        Status = segment.Status switch
        {
            TargetingResourceStatus.Active => ProtocolStatus.Active,
            TargetingResourceStatus.Archived => ProtocolStatus.Archived,
            _ => ProtocolStatus.Unspecified,
        },
        Version = segment.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(segment.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(segment.UpdatedAt),
        ArchivedAt = segment.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolAttribute ToProtocol(this TargetingAttributeMetadata attribute) => new()
    {
        Key = attribute.Key,
        DisplayName = attribute.DisplayName,
        ValueKind = attribute.ValueKind.ToProtocol(),
        BuiltIn = attribute.BuiltIn,
        Required = attribute.Required,
    };

    public static ProtocolOperatorDefinition ToProtocol(
        this TargetingOperatorMetadata metadata)
    {
        var result = new ProtocolOperatorDefinition
        {
            Operator = metadata.Operator.ToProtocol(),
            DisplayName = metadata.DisplayName,
            MinimumValues = metadata.MinimumValues,
            MaximumValues = metadata.MaximumValues,
        };
        result.SupportedValueKinds.AddRange(
            metadata.SupportedValueKinds.Select(ToProtocol));
        return result;
    }

    public static ProtocolSimulationResult ToProtocol(
        this TargetingSimulationOutcome outcome)
    {
        var result = new ProtocolSimulationResult
        {
            SegmentId = outcome.SegmentId.ToString("D"),
            SegmentKey = outcome.SegmentKey,
            SegmentVersion = outcome.SegmentVersion,
            Matched = outcome.Matched,
            Reason = outcome.Reason,
            BucketEvaluated = outcome.BucketEvaluated,
            Bucket = outcome.Bucket,
            SelectedVariant = outcome.SelectedVariant,
            BucketNamespace = outcome.BucketNamespace,
            BucketingVersion = outcome.BucketingVersion,
        };
        result.ConditionTraces.AddRange(outcome.ConditionTraces.Select(trace => new
            Asterloom.Protocol.Targeting.V1.TargetingConditionTrace
            {
                ConditionId = trace.ConditionId,
                Matched = trace.Matched,
                Reason = trace.Reason.ToProtocol(),
            }));
        return result;
    }

    public static TargetingRule ToDomain(this ProtocolRule? rule)
    {
        if (rule is null)
        {
            throw Invalid("rule", "A targeting rule is required.");
        }

        return new TargetingRule(
            rule.MatchMode switch
            {
                ProtocolMatchMode.All => TargetingMatchMode.All,
                ProtocolMatchMode.Any => TargetingMatchMode.Any,
                _ => (TargetingMatchMode)0,
            },
            rule.Conditions.Select(ToDomain).ToArray());
    }

    public static TargetingEvaluationContext ToDomain(
        this ProtocolContext? context,
        Guid applicationId,
        Guid environmentId)
    {
        if (context is null)
        {
            throw Invalid("context", "An evaluation context is required.");
        }

        var attributes = new Dictionary<string, TargetingValue>(StringComparer.Ordinal);
        foreach (var attribute in context.Attributes)
        {
            if (!attributes.TryAdd(attribute.Key, attribute.Value.ToDomain()))
            {
                throw Invalid(
                    "context.attributes",
                    $"Custom attribute '{attribute.Key}' is duplicated.");
            }
        }

        return new TargetingEvaluationContext(
            context.TargetingKey,
            applicationId,
            environmentId,
            EmptyToNull(context.UserId),
            EmptyToNull(context.ClientVersion),
            EmptyToNull(context.Platform),
            EmptyToNull(context.Region),
            EmptyToNull(context.Language),
            attributes);
    }

    public static TargetingBucketPreviewRequest? ToDomain(
        this Asterloom.Protocol.Targeting.V1.BucketPreview? preview)
    {
        if (preview is null)
        {
            return null;
        }

        return new TargetingBucketPreviewRequest(
            preview.ResourceType,
            preview.ResourceKey,
            preview.Salt,
            preview.Allocations.Select(ToDomain).ToArray());
    }

    private static TargetingCondition ToDomain(ProtocolCondition condition) => new(
        condition.Id,
        condition.Attribute,
        condition.ValueKind.ToDomain(),
        condition.Operator.ToDomain(),
        condition.Values.Select(ToDomain).ToArray(),
        condition.CaseSensitive);

    private static TargetingValue ToDomain(this ProtocolValue? value)
    {
        if (value is null)
        {
            throw Invalid("value", "A targeting value is required.");
        }

        return value.ValueCase switch
        {
            ProtocolValue.ValueOneofCase.Text => TargetingValue.From(value.Text),
            ProtocolValue.ValueOneofCase.Truth => TargetingValue.From(value.Truth),
            ProtocolValue.ValueOneofCase.Numeric => TargetingValue.From(value.Numeric),
            _ => throw Invalid("value", "A targeting value must have a typed value."),
        };
    }

    private static TargetingBucketAllocation ToDomain(ProtocolAllocation allocation) =>
        new(allocation.Variant, allocation.Start, allocation.End);

    private static ProtocolRule ToProtocol(this TargetingRule rule)
    {
        var result = new ProtocolRule
        {
            MatchMode = rule.MatchMode switch
            {
                TargetingMatchMode.All => ProtocolMatchMode.All,
                TargetingMatchMode.Any => ProtocolMatchMode.Any,
                _ => ProtocolMatchMode.Unspecified,
            },
        };
        result.Conditions.AddRange(rule.Conditions.Select(ToProtocol));
        return result;
    }

    private static ProtocolCondition ToProtocol(TargetingCondition condition)
    {
        var result = new ProtocolCondition
        {
            Id = condition.Id,
            Attribute = condition.Attribute,
            ValueKind = condition.ValueKind.ToProtocol(),
            Operator = condition.Operator.ToProtocol(),
            CaseSensitive = condition.CaseSensitive,
        };
        result.Values.AddRange(condition.Values.Select(ToProtocol));
        return result;
    }

    private static ProtocolValue ToProtocol(TargetingValue value) => value.Kind switch
    {
        TargetingValueKind.Text => new ProtocolValue { Text = value.StringValue! },
        TargetingValueKind.Truth => new ProtocolValue { Truth = value.BooleanValue!.Value },
        TargetingValueKind.Numeric => new ProtocolValue { Numeric = value.NumberValue!.Value },
        _ => new ProtocolValue(),
    };

    private static ProtocolValueKind ToProtocol(this TargetingValueKind kind) => kind switch
    {
        TargetingValueKind.Text => ProtocolValueKind.Text,
        TargetingValueKind.Truth => ProtocolValueKind.Truth,
        TargetingValueKind.Numeric => ProtocolValueKind.Numeric,
        _ => ProtocolValueKind.Unspecified,
    };

    private static TargetingValueKind ToDomain(this ProtocolValueKind kind) => kind switch
    {
        ProtocolValueKind.Text => TargetingValueKind.Text,
        ProtocolValueKind.Truth => TargetingValueKind.Truth,
        ProtocolValueKind.Numeric => TargetingValueKind.Numeric,
        _ => (TargetingValueKind)0,
    };

    private static ProtocolOperator ToProtocol(this TargetingOperator value) =>
        (ProtocolOperator)(int)value;

    private static TargetingOperator ToDomain(this ProtocolOperator value) =>
        (TargetingOperator)(int)value;

    private static ProtocolConditionReason ToProtocol(this TargetingConditionReason value) =>
        (ProtocolConditionReason)(int)value;

    private static string? EmptyToNull(string value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static AsterloomException Invalid(string field, string message) =>
        new(
            AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [field] = [message],
            });
}
