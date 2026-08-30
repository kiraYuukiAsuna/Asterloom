using Asterloom.Modules.Errors;
using Asterloom.Modules.Feature.Model;
using Google.Protobuf.WellKnownTypes;
using ProtocolAllocation = Asterloom.Protocol.Feature.V1.FeatureAllocation;
using ProtocolDefinition = Asterloom.Protocol.Feature.V1.FeatureDefinition;
using ProtocolEvaluation = Asterloom.Protocol.Feature.V1.FeatureEvaluationDetails;
using ProtocolEvaluationReason = Asterloom.Protocol.Feature.V1.FeatureEvaluationReason;
using ProtocolFlag = Asterloom.Protocol.Feature.V1.FeatureFlag;
using ProtocolPrerequisite = Asterloom.Protocol.Feature.V1.FeaturePrerequisite;
using ProtocolRevision = Asterloom.Protocol.Feature.V1.FeatureRevision;
using ProtocolRule = Asterloom.Protocol.Feature.V1.FeatureTargetingRule;
using ProtocolSeverity = Asterloom.Protocol.Feature.V1.FeatureValidationSeverity;
using ProtocolStatus = Asterloom.Protocol.Feature.V1.FeatureResourceStatus;
using ProtocolValidation = Asterloom.Protocol.Feature.V1.FeatureValidationResult;
using ProtocolValue = Asterloom.Protocol.Feature.V1.FeatureValue;
using ProtocolValueKind = Asterloom.Protocol.Feature.V1.FeatureValueKind;
using ProtocolVariant = Asterloom.Protocol.Feature.V1.FeatureVariant;

namespace Asterloom.Modules.Feature;

public static class FeatureProtocolMapper
{
    public static ProtocolFlag ToProtocol(this FeatureFlag flag)
    {
        var result = new ProtocolFlag
        {
            Id = flag.Id.ToString("D"),
            TenantId = flag.TenantId.ToString("D"),
            ApplicationId = flag.ApplicationId.ToString("D"),
            EnvironmentId = flag.EnvironmentId.ToString("D"),
            Key = flag.Key,
            DisplayName = flag.DisplayName,
            Description = flag.Description,
            ValueKind = flag.ValueKind.ToProtocol(),
            Status = flag.Status switch
            {
                FeatureResourceStatus.Active => ProtocolStatus.Active,
                FeatureResourceStatus.Archived => ProtocolStatus.Archived,
                _ => ProtocolStatus.Unspecified,
            },
            DraftDefinition = flag.DraftDefinition.ToProtocol(),
            DraftRevision = flag.DraftRevision,
            PublishedRevision = flag.PublishedRevision ?? 0,
            Version = flag.Version,
            CreatedAt = Timestamp.FromDateTimeOffset(flag.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(flag.UpdatedAt),
            ArchivedAt = flag.ArchivedAt is { } archivedAt
                ? Timestamp.FromDateTimeOffset(archivedAt)
                : null,
            PublishedAt = flag.PublishedAt is { } publishedAt
                ? Timestamp.FromDateTimeOffset(publishedAt)
                : null,
        };
        if (flag.PublishedDefinition is not null)
        {
            result.PublishedDefinition = flag.PublishedDefinition.ToProtocol();
        }

        return result;
    }

    public static ProtocolRevision ToProtocol(this FeatureRevision revision) => new()
    {
        Id = revision.Id.ToString("D"),
        FlagId = revision.FlagId.ToString("D"),
        Revision = revision.Revision,
        Definition = revision.Definition.ToProtocol(),
        SourceRevision = revision.SourceRevision ?? 0,
        PublishedAt = Timestamp.FromDateTimeOffset(revision.PublishedAt),
    };

    public static ProtocolValidation ToProtocol(this FeatureValidationResult validation)
    {
        var result = new ProtocolValidation
        {
            Valid = validation.Valid,
            DefinitionHash = validation.DefinitionHash,
        };
        result.Issues.AddRange(validation.Issues.Select(issue => new
            Asterloom.Protocol.Feature.V1.FeatureValidationIssue
            {
                Severity = issue.Severity switch
                {
                    FeatureValidationSeverity.Error => ProtocolSeverity.Error,
                    FeatureValidationSeverity.Warning => ProtocolSeverity.Warning,
                    _ => ProtocolSeverity.Unspecified,
                },
                Code = issue.Code,
                Path = issue.Path,
                Message = issue.Message,
            }));
        return result;
    }

    public static ProtocolEvaluation ToProtocol(this FeatureEvaluationDetails details)
    {
        var result = new ProtocolEvaluation
        {
            FlagId = details.FlagId.ToString("D"),
            FlagKey = details.FlagKey,
            Revision = details.Revision,
            Value = details.Value.ToProtocol(),
            VariantKey = details.VariantKey,
            Reason = details.Reason switch
            {
                FeatureEvaluationReason.Disabled => ProtocolEvaluationReason.Disabled,
                FeatureEvaluationReason.TargetingMatch => ProtocolEvaluationReason.TargetingMatch,
                FeatureEvaluationReason.Split => ProtocolEvaluationReason.Split,
                FeatureEvaluationReason.Default => ProtocolEvaluationReason.Default,
                FeatureEvaluationReason.PrerequisiteFailed =>
                    ProtocolEvaluationReason.PrerequisiteFailed,
                _ => ProtocolEvaluationReason.Unspecified,
            },
            Bucket = details.Bucket,
            BucketEvaluated = details.BucketEvaluated,
            BucketingVersion = details.BucketingVersion,
            UsedDraft = details.UsedDraft,
        };
        result.Trace.AddRange(details.Trace);
        return result;
    }

    public static FeatureDefinition ToDomain(this ProtocolDefinition? definition)
    {
        if (definition is null)
        {
            throw Invalid("definition", "A feature definition is required.");
        }

        try
        {
            return new FeatureDefinition(
                definition.Enabled,
                definition.DefaultVariantKey,
                definition.Variants.Select(static variant => new FeatureVariant(
                    variant.Key,
                    variant.DisplayName,
                    variant.Value.ToDomain())).ToArray(),
                definition.Prerequisites.Select(static prerequisite =>
                    new FeaturePrerequisite(
                        prerequisite.FlagKey,
                        prerequisite.ExpectedVariantKey)).ToArray(),
                definition.TargetingRules.Select(static rule =>
                    new FeatureTargetingRule(
                        rule.Id,
                        Guid.TryParse(rule.SegmentId, out var id) ? id : Guid.Empty,
                        rule.VariantKey)).ToArray(),
                definition.Allocations.Select(static allocation =>
                    new FeatureAllocation(
                        allocation.VariantKey,
                        allocation.Start,
                        allocation.End)).ToArray(),
                definition.BucketingSalt);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("definition", exception.Message);
        }
    }

    public static FeatureValueKind ToDomain(this ProtocolValueKind kind) => kind switch
    {
        ProtocolValueKind.Boolean => FeatureValueKind.Truth,
        ProtocolValueKind.String => FeatureValueKind.Text,
        ProtocolValueKind.Integer => FeatureValueKind.WholeNumber,
        ProtocolValueKind.Double => FeatureValueKind.DecimalNumber,
        ProtocolValueKind.Object => FeatureValueKind.Structure,
        _ => (FeatureValueKind)0,
    };

    private static FeatureValue ToDomain(this ProtocolValue? value)
    {
        if (value is null)
        {
            throw Invalid("value", "A typed feature value is required.");
        }

        try
        {
            return value.ValueCase switch
            {
                ProtocolValue.ValueOneofCase.BooleanValue =>
                    FeatureValue.From(value.BooleanValue),
                ProtocolValue.ValueOneofCase.StringValue =>
                    FeatureValue.From(value.StringValue),
                ProtocolValue.ValueOneofCase.IntegerValue =>
                    FeatureValue.From(value.IntegerValue),
                ProtocolValue.ValueOneofCase.DoubleValue =>
                    FeatureValue.From(value.DoubleValue),
                ProtocolValue.ValueOneofCase.ObjectJson =>
                    FeatureValue.FromJson(value.ObjectJson),
                _ => throw new ArgumentException("A typed feature value is required."),
            };
        }
        catch (ArgumentException exception)
        {
            throw Invalid("value", exception.Message);
        }
    }

    private static ProtocolDefinition ToProtocol(this FeatureDefinition definition)
    {
        var result = new ProtocolDefinition
        {
            Enabled = definition.Enabled,
            DefaultVariantKey = definition.DefaultVariantKey,
            BucketingSalt = definition.BucketingSalt,
        };
        result.Variants.AddRange(definition.Variants.Select(variant => new ProtocolVariant
        {
            Key = variant.Key,
            DisplayName = variant.DisplayName,
            Value = variant.Value.ToProtocol(),
        }));
        result.Prerequisites.AddRange(definition.Prerequisites.Select(prerequisite =>
            new ProtocolPrerequisite
            {
                FlagKey = prerequisite.FlagKey,
                ExpectedVariantKey = prerequisite.ExpectedVariantKey,
            }));
        result.TargetingRules.AddRange(definition.TargetingRules.Select(rule => new ProtocolRule
        {
            Id = rule.Id,
            SegmentId = rule.SegmentId.ToString("D"),
            VariantKey = rule.VariantKey,
        }));
        result.Allocations.AddRange(definition.Allocations.Select(allocation =>
            new ProtocolAllocation
            {
                VariantKey = allocation.VariantKey,
                Start = allocation.Start,
                End = allocation.End,
            }));
        return result;
    }

    private static ProtocolValue ToProtocol(this FeatureValue value) => value.Kind switch
    {
        FeatureValueKind.Truth => new ProtocolValue
        {
            BooleanValue = value.BooleanValue!.Value,
        },
        FeatureValueKind.Text => new ProtocolValue { StringValue = value.StringValue },
        FeatureValueKind.WholeNumber => new ProtocolValue
        {
            IntegerValue = value.IntegerValue!.Value,
        },
        FeatureValueKind.DecimalNumber => new ProtocolValue
        {
            DoubleValue = value.DoubleValue!.Value,
        },
        FeatureValueKind.Structure => new ProtocolValue { ObjectJson = value.ObjectJson },
        _ => new ProtocolValue(),
    };

    private static ProtocolValueKind ToProtocol(this FeatureValueKind kind) => kind switch
    {
        FeatureValueKind.Truth => ProtocolValueKind.Boolean,
        FeatureValueKind.Text => ProtocolValueKind.String,
        FeatureValueKind.WholeNumber => ProtocolValueKind.Integer,
        FeatureValueKind.DecimalNumber => ProtocolValueKind.Double,
        FeatureValueKind.Structure => ProtocolValueKind.Object,
        _ => ProtocolValueKind.Unspecified,
    };

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
