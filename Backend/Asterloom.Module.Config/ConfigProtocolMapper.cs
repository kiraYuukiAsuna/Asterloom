using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Errors;
using Google.Protobuf.WellKnownTypes;
using ProtocolDefinition = Asterloom.Protocol.Config.V1.ConfigDefinition;
using ProtocolDiff = Asterloom.Protocol.Config.V1.ConfigDiff;
using ProtocolEffectiveValue = Asterloom.Protocol.Config.V1.ConfigEffectiveValue;
using ProtocolEntry = Asterloom.Protocol.Config.V1.ConfigEntry;
using ProtocolEvaluationReason = Asterloom.Protocol.Config.V1.ConfigEvaluationReason;
using ProtocolResourceStatus = Asterloom.Protocol.Config.V1.ConfigResourceStatus;
using ProtocolRevision = Asterloom.Protocol.Config.V1.ConfigRevision;
using ProtocolSnapshotMetadata = Asterloom.Protocol.Config.V1.ConfigSnapshotMetadata;
using ProtocolSnapshotResponse = Asterloom.Protocol.Config.V1.ConfigSnapshotResponse;
using ProtocolTargetingRule = Asterloom.Protocol.Config.V1.ConfigTargetingRule;
using ProtocolUpdateStatus = Asterloom.Protocol.Config.V1.ConfigUpdateStatus;
using ProtocolValidation = Asterloom.Protocol.Config.V1.ConfigValidationResult;
using ProtocolValidationSeverity = Asterloom.Protocol.Config.V1.ConfigValidationSeverity;
using ProtocolValue = Asterloom.Protocol.Config.V1.ConfigValue;
using ProtocolValueKind = Asterloom.Protocol.Config.V1.ConfigValueKind;
using ProtocolVisibility = Asterloom.Protocol.Config.V1.ConfigVisibility;

namespace Asterloom.Modules.Config;

public static class ConfigProtocolMapper
{
    public static ProtocolEntry ToProtocol(this ConfigEntry entry)
    {
        var result = new ProtocolEntry
        {
            Id = entry.Id.ToString("D"),
            TenantId = entry.TenantId.ToString("D"),
            ApplicationId = entry.ApplicationId.ToString("D"),
            EnvironmentId = entry.EnvironmentId.ToString("D"),
            Key = entry.Key,
            DisplayName = entry.DisplayName,
            Description = entry.Description,
            ValueKind = entry.ValueKind.ToProtocol(),
            Visibility = entry.Visibility.ToProtocol(),
            Status = entry.Status switch
            {
                ConfigResourceStatus.Active => ProtocolResourceStatus.Active,
                ConfigResourceStatus.Archived => ProtocolResourceStatus.Archived,
                _ => ProtocolResourceStatus.Unspecified,
            },
            DraftDefinition = entry.DraftDefinition.ToProtocol(),
            DraftRevision = entry.DraftRevision,
            PublishedRevision = entry.PublishedRevision ?? 0,
            PublishedSnapshotVersion = entry.PublishedSnapshotVersion ?? 0,
            Version = entry.Version,
            CreatedAt = Timestamp.FromDateTimeOffset(entry.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(entry.UpdatedAt),
            ArchivedAt = entry.ArchivedAt is { } archivedAt
                ? Timestamp.FromDateTimeOffset(archivedAt)
                : null,
            PublishedAt = entry.PublishedAt is { } publishedAt
                ? Timestamp.FromDateTimeOffset(publishedAt)
                : null,
        };
        if (entry.PublishedDefinition is not null)
        {
            result.PublishedDefinition = entry.PublishedDefinition.ToProtocol();
        }
        return result;
    }

    public static ProtocolRevision ToProtocol(this ConfigRevision revision) => new()
    {
        Id = revision.Id.ToString("D"),
        EntryId = revision.EntryId.ToString("D"),
        Revision = revision.Revision,
        Definition = revision.Definition.ToProtocol(),
        SourceRevision = revision.SourceRevision ?? 0,
        SnapshotVersion = revision.SnapshotVersion,
        PublishedAt = Timestamp.FromDateTimeOffset(revision.PublishedAt),
    };

    public static ProtocolSnapshotMetadata ToProtocol(this ConfigSnapshot snapshot) => new()
    {
        Id = snapshot.Id.ToString("D"),
        TenantId = snapshot.TenantId.ToString("D"),
        ApplicationId = snapshot.ApplicationId.ToString("D"),
        EnvironmentId = snapshot.EnvironmentId.ToString("D"),
        Version = snapshot.Version,
        EntryCount = snapshot.Items.Count,
        CreatedAt = Timestamp.FromDateTimeOffset(snapshot.CreatedAt),
    };

    public static ProtocolValidation ToProtocol(this ConfigValidationResult validation)
    {
        var result = new ProtocolValidation
        {
            Valid = validation.Valid,
            DefinitionHash = validation.DefinitionHash,
        };
        result.Issues.AddRange(validation.Issues.Select(issue => new
            Asterloom.Protocol.Config.V1.ConfigValidationIssue
            {
                Severity = issue.Severity switch
                {
                    ConfigValidationSeverity.Error => ProtocolValidationSeverity.Error,
                    ConfigValidationSeverity.Warning => ProtocolValidationSeverity.Warning,
                    _ => ProtocolValidationSeverity.Unspecified,
                },
                Code = issue.Code,
                Path = issue.Path,
                Message = issue.Message,
            }));
        return result;
    }

    public static ProtocolDiff ToProtocol(this ConfigDiff diff)
    {
        var result = new ProtocolDiff
        {
            Changed = diff.Changed,
            PublishedJson = diff.PublishedJson,
            DraftJson = diff.DraftJson,
        };
        result.ChangedPaths.AddRange(diff.ChangedPaths);
        return result;
    }

    public static ProtocolEffectiveValue ToProtocol(this ConfigEffectiveValue value) => new()
    {
        EntryId = value.EntryId.ToString("D"),
        Key = value.Key,
        ValueKind = value.ValueKind.ToProtocol(),
        Value = value.Value.ToProtocol(),
        Revision = value.Revision,
        Reason = value.Reason switch
        {
            ConfigEvaluationReason.TargetingMatch => ProtocolEvaluationReason.TargetingMatch,
            ConfigEvaluationReason.Default => ProtocolEvaluationReason.Default,
            _ => ProtocolEvaluationReason.Unspecified,
        },
        TargetingRuleId = value.TargetingRuleId ?? string.Empty,
    };

    public static ProtocolSnapshotResponse ToProtocol(this ConfigSnapshotResult snapshot)
    {
        var result = new ProtocolSnapshotResponse
        {
            SnapshotVersion = snapshot.SnapshotVersion,
            Etag = snapshot.ETag,
            NotModified = snapshot.NotModified,
            GeneratedAt = Timestamp.FromDateTimeOffset(snapshot.GeneratedAt),
        };
        result.Values.AddRange(snapshot.Values.Select(ToProtocol));
        return result;
    }

    public static ProtocolUpdateStatus ToProtocol(this ConfigUpdateStatus status) => new()
    {
        Changed = status.Changed,
        CurrentSnapshotVersion = status.CurrentSnapshotVersion,
        Etag = status.ETag,
        CheckedAt = Timestamp.FromDateTimeOffset(status.CheckedAt),
    };

    public static ConfigDefinition ToDomain(this ProtocolDefinition? definition)
    {
        if (definition is null)
        {
            throw Invalid("definition", "A configuration definition is required.");
        }
        try
        {
            return new(
                definition.SchemaJson,
                definition.DefaultValue.ToDomain(),
                definition.TargetingRules.Select(rule => new ConfigTargetingRule(
                    rule.Id,
                    Guid.TryParse(rule.SegmentId, out var id) ? id : Guid.Empty,
                    rule.Value.ToDomain())).ToArray());
        }
        catch (ArgumentException exception)
        {
            throw Invalid("definition", exception.Message);
        }
    }

    public static ConfigValueKind ToDomain(this ProtocolValueKind kind) => kind switch
    {
        ProtocolValueKind.Boolean => ConfigValueKind.Truth,
        ProtocolValueKind.Integer => ConfigValueKind.WholeNumber,
        ProtocolValueKind.Double => ConfigValueKind.DecimalNumber,
        ProtocolValueKind.String => ConfigValueKind.Text,
        ProtocolValueKind.Json => ConfigValueKind.Structure,
        _ => (ConfigValueKind)0,
    };

    public static ConfigVisibility ToDomain(this ProtocolVisibility visibility) => visibility switch
    {
        ProtocolVisibility.Client => ConfigVisibility.Client,
        ProtocolVisibility.Server => ConfigVisibility.Server,
        _ => (ConfigVisibility)0,
    };

    private static ConfigValue ToDomain(this ProtocolValue? value)
    {
        if (value is null)
        {
            throw Invalid("value", "A typed configuration value is required.");
        }
        try
        {
            return value.ValueCase switch
            {
                ProtocolValue.ValueOneofCase.BooleanValue =>
                    ConfigValue.From(value.BooleanValue),
                ProtocolValue.ValueOneofCase.IntegerValue =>
                    ConfigValue.From(value.IntegerValue),
                ProtocolValue.ValueOneofCase.DoubleValue =>
                    ConfigValue.From(value.DoubleValue),
                ProtocolValue.ValueOneofCase.StringValue =>
                    ConfigValue.From(value.StringValue),
                ProtocolValue.ValueOneofCase.JsonValue =>
                    ConfigValue.FromJson(value.JsonValue),
                _ => throw new ArgumentException("A typed configuration value is required."),
            };
        }
        catch (ArgumentException exception)
        {
            throw Invalid("value", exception.Message);
        }
    }

    private static ProtocolDefinition ToProtocol(this ConfigDefinition definition)
    {
        var result = new ProtocolDefinition
        {
            SchemaJson = definition.SchemaJson,
            DefaultValue = definition.DefaultValue.ToProtocol(),
        };
        result.TargetingRules.AddRange(definition.TargetingRules.Select(rule =>
            new ProtocolTargetingRule
            {
                Id = rule.Id,
                SegmentId = rule.SegmentId.ToString("D"),
                Value = rule.Value.ToProtocol(),
            }));
        return result;
    }

    private static ProtocolValue ToProtocol(this ConfigValue value) => value.Kind switch
    {
        ConfigValueKind.Truth => new ProtocolValue
        {
            BooleanValue = value.BooleanValue!.Value,
        },
        ConfigValueKind.WholeNumber => new ProtocolValue
        {
            IntegerValue = value.IntegerValue!.Value,
        },
        ConfigValueKind.DecimalNumber => new ProtocolValue
        {
            DoubleValue = value.DoubleValue!.Value,
        },
        ConfigValueKind.Text => new ProtocolValue { StringValue = value.StringValue },
        ConfigValueKind.Structure => new ProtocolValue { JsonValue = value.JsonValue },
        _ => new ProtocolValue(),
    };

    private static ProtocolValueKind ToProtocol(this ConfigValueKind kind) => kind switch
    {
        ConfigValueKind.Truth => ProtocolValueKind.Boolean,
        ConfigValueKind.WholeNumber => ProtocolValueKind.Integer,
        ConfigValueKind.DecimalNumber => ProtocolValueKind.Double,
        ConfigValueKind.Text => ProtocolValueKind.String,
        ConfigValueKind.Structure => ProtocolValueKind.Json,
        _ => ProtocolValueKind.Unspecified,
    };

    private static ProtocolVisibility ToProtocol(this ConfigVisibility visibility) =>
        visibility switch
        {
            ConfigVisibility.Client => ProtocolVisibility.Client,
            ConfigVisibility.Server => ProtocolVisibility.Server,
            _ => ProtocolVisibility.Unspecified,
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
