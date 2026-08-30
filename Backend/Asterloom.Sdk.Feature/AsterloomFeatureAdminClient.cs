using Asterloom.Protocol.Feature.Admin.V1;
using Asterloom.Targeting;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProtocolContext = Asterloom.Protocol.Targeting.V1.EvaluationContext;
using ProtocolDefinition = Asterloom.Protocol.Feature.V1.FeatureDefinition;
using ProtocolEvaluation = Asterloom.Protocol.Feature.V1.FeatureEvaluationDetails;
using ProtocolEvaluationReason = Asterloom.Protocol.Feature.V1.FeatureEvaluationReason;
using ProtocolFlag = Asterloom.Protocol.Feature.V1.FeatureFlag;
using ProtocolResourceStatus = Asterloom.Protocol.Feature.V1.FeatureResourceStatus;
using ProtocolRevision = Asterloom.Protocol.Feature.V1.FeatureRevision;
using ProtocolSeverity = Asterloom.Protocol.Feature.V1.FeatureValidationSeverity;
using ProtocolValue = Asterloom.Protocol.Feature.V1.FeatureValue;
using ProtocolValueKind = Asterloom.Protocol.Feature.V1.FeatureValueKind;

namespace Asterloom.Sdk.Feature;

public sealed class AsterloomFeatureAdminClient
{
    private const int MaximumPageSize = 100;

    private readonly FeatureAdminService.FeatureAdminServiceClient _client;

    public AsterloomFeatureAdminClient(CallInvoker callInvoker)
        : this(new FeatureAdminService.FeatureAdminServiceClient(
            callInvoker ?? throw new ArgumentNullException(nameof(callInvoker))))
    {
    }

    public AsterloomFeatureAdminClient(
        FeatureAdminService.FeatureAdminServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<AsterloomFeaturePage<AsterloomFeatureFlag>> ListFlagsAsync(
        AsterloomFeatureScope scope,
        string? query = null,
        bool includeArchived = false,
        int pageSize = MaximumPageSize,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        var response = await _client.ListFlagsAsync(
            new ListFlagsRequest
            {
                TenantId = scope.TenantId.ToString("D"),
                ApplicationId = scope.ApplicationId.ToString("D"),
                EnvironmentId = scope.EnvironmentId.ToString("D"),
                Query = NormalizeOptional(query, 200),
                IncludeArchived = includeArchived,
                PageSize = ValidatePageSize(pageSize),
                PageToken = NormalizeOptional(pageToken, 2_048),
            },
            cancellationToken: cancellationToken);
        return new(
            response.Flags.Select(ToModel).ToArray(),
            EmptyToNull(response.NextPageToken));
    }

    public async Task<AsterloomFeatureFlag> GetFlagAsync(
        AsterloomFeatureScope scope,
        Guid flagId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        return ToModel(await _client.GetFlagAsync(
            new GetFlagRequest
            {
                TenantId = scope.TenantId.ToString("D"),
                ApplicationId = scope.ApplicationId.ToString("D"),
                EnvironmentId = scope.EnvironmentId.ToString("D"),
                FlagId = FormatId(flagId, nameof(flagId)),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomFeatureFlag> CreateFlagAsync(
        AsterloomFeatureScope scope,
        AsterloomFeatureRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        ArgumentNullException.ThrowIfNull(registration);
        ValidateDefinition(registration.Definition, registration.ValueKind);
        var response = await _client.CreateFlagAsync(
            new CreateFlagRequest
            {
                TenantId = scope.TenantId.ToString("D"),
                ApplicationId = scope.ApplicationId.ToString("D"),
                EnvironmentId = scope.EnvironmentId.ToString("D"),
                Key = RequireIdentifier(registration.Key, nameof(registration)),
                DisplayName = RequireText(registration.DisplayName, nameof(registration), 200),
                Description = NormalizeOptional(registration.Description, 1_000),
                ValueKind = ToProtocol(registration.ValueKind),
                Definition = ToProtocol(registration.Definition),
            },
            cancellationToken: cancellationToken);
        return ToModel(response);
    }

    public async Task<AsterloomFeatureFlag> UpdateFlagDraftAsync(
        AsterloomFeatureFlag flag,
        AsterloomFeatureDraftUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentNullException.ThrowIfNull(update);
        ValidateScope(flag.Scope);
        ValidateDefinition(update.Definition, flag.ValueKind);
        var response = await _client.UpdateFlagDraftAsync(
            new UpdateFlagDraftRequest
            {
                TenantId = flag.Scope.TenantId.ToString("D"),
                ApplicationId = flag.Scope.ApplicationId.ToString("D"),
                EnvironmentId = flag.Scope.EnvironmentId.ToString("D"),
                FlagId = FormatId(flag.Id, nameof(flag)),
                DisplayName = RequireText(update.DisplayName, nameof(update), 200),
                Description = NormalizeOptional(update.Description, 1_000),
                Definition = ToProtocol(update.Definition),
                ExpectedVersion = ValidateVersion(flag.Version, nameof(flag)),
            },
            cancellationToken: cancellationToken);
        return ToModel(response);
    }

    public async Task<AsterloomFeatureValidationResult> ValidateFlagDraftAsync(
        AsterloomFeatureFlag flag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ValidateScope(flag.Scope);
        var response = await _client.ValidateFlagDraftAsync(
            new ValidateFlagDraftRequest
            {
                TenantId = flag.Scope.TenantId.ToString("D"),
                ApplicationId = flag.Scope.ApplicationId.ToString("D"),
                EnvironmentId = flag.Scope.EnvironmentId.ToString("D"),
                FlagId = FormatId(flag.Id, nameof(flag)),
            },
            cancellationToken: cancellationToken);
        return new(
            response.Valid,
            response.Issues.Select(issue => new AsterloomFeatureValidationIssue(
                ToModel(issue.Severity),
                issue.Code,
                issue.Path,
                issue.Message)).ToArray(),
            response.DefinitionHash);
    }

    public async Task<AsterloomFeatureFlag> PublishFlagAsync(
        AsterloomFeatureFlag flag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ValidateScope(flag.Scope);
        return ToModel(await _client.PublishFlagAsync(
            new PublishFlagRequest
            {
                TenantId = flag.Scope.TenantId.ToString("D"),
                ApplicationId = flag.Scope.ApplicationId.ToString("D"),
                EnvironmentId = flag.Scope.EnvironmentId.ToString("D"),
                FlagId = FormatId(flag.Id, nameof(flag)),
                ExpectedVersion = ValidateVersion(flag.Version, nameof(flag)),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomFeaturePage<AsterloomFeatureRevision>> ListFlagRevisionsAsync(
        AsterloomFeatureFlag flag,
        int pageSize = MaximumPageSize,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ValidateScope(flag.Scope);
        var response = await _client.ListFlagRevisionsAsync(
            new ListFlagRevisionsRequest
            {
                TenantId = flag.Scope.TenantId.ToString("D"),
                ApplicationId = flag.Scope.ApplicationId.ToString("D"),
                EnvironmentId = flag.Scope.EnvironmentId.ToString("D"),
                FlagId = FormatId(flag.Id, nameof(flag)),
                PageSize = ValidatePageSize(pageSize),
                PageToken = NormalizeOptional(pageToken, 2_048),
            },
            cancellationToken: cancellationToken);
        return new(
            response.Revisions.Select(ToModel).ToArray(),
            EmptyToNull(response.NextPageToken));
    }

    public async Task<AsterloomFeatureFlag> RollbackFlagAsync(
        AsterloomFeatureFlag flag,
        long revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ValidateScope(flag.Scope);
        return ToModel(await _client.RollbackFlagAsync(
            new RollbackFlagRequest
            {
                TenantId = flag.Scope.TenantId.ToString("D"),
                ApplicationId = flag.Scope.ApplicationId.ToString("D"),
                EnvironmentId = flag.Scope.EnvironmentId.ToString("D"),
                FlagId = FormatId(flag.Id, nameof(flag)),
                Revision = ValidateVersion(revision, nameof(revision)),
                ExpectedVersion = ValidateVersion(flag.Version, nameof(flag)),
            },
            cancellationToken: cancellationToken));
    }

    public Task<AsterloomFeatureFlag> ArchiveFlagAsync(
        AsterloomFeatureFlag flag,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(flag, restore: false, cancellationToken);

    public Task<AsterloomFeatureFlag> RestoreFlagAsync(
        AsterloomFeatureFlag flag,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(flag, restore: true, cancellationToken);

    public async Task<AsterloomFeatureEvaluationDetails> SimulateFlagAsync(
        AsterloomFeatureFlag flag,
        TargetingEvaluationContext context,
        bool useDraft = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentNullException.ThrowIfNull(context);
        ValidateScope(flag.Scope);
        if (context.ApplicationId != flag.Scope.ApplicationId
            || context.EnvironmentId != flag.Scope.EnvironmentId)
        {
            throw new ArgumentException(
                "The context application and environment must match the flag scope.",
                nameof(context));
        }

        TargetingContract.ValidateContext(context);
        var response = await _client.SimulateFlagAsync(
            new SimulateFlagRequest
            {
                TenantId = flag.Scope.TenantId.ToString("D"),
                ApplicationId = flag.Scope.ApplicationId.ToString("D"),
                EnvironmentId = flag.Scope.EnvironmentId.ToString("D"),
                FlagId = FormatId(flag.Id, nameof(flag)),
                UseDraft = useDraft,
                Context = ToProtocol(context),
            },
            cancellationToken: cancellationToken);
        return ToModel(response);
    }

    private async Task<AsterloomFeatureFlag> ChangeStatusAsync(
        AsterloomFeatureFlag flag,
        bool restore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ValidateScope(flag.Scope);
        ProtocolFlag response;
        if (restore)
        {
            response = await _client.RestoreFlagAsync(
                new RestoreFlagRequest
                {
                    TenantId = flag.Scope.TenantId.ToString("D"),
                    ApplicationId = flag.Scope.ApplicationId.ToString("D"),
                    EnvironmentId = flag.Scope.EnvironmentId.ToString("D"),
                    FlagId = FormatId(flag.Id, nameof(flag)),
                    ExpectedVersion = ValidateVersion(flag.Version, nameof(flag)),
                },
                cancellationToken: cancellationToken);
        }
        else
        {
            response = await _client.ArchiveFlagAsync(
                new ArchiveFlagRequest
                {
                    TenantId = flag.Scope.TenantId.ToString("D"),
                    ApplicationId = flag.Scope.ApplicationId.ToString("D"),
                    EnvironmentId = flag.Scope.EnvironmentId.ToString("D"),
                    FlagId = FormatId(flag.Id, nameof(flag)),
                    ExpectedVersion = ValidateVersion(flag.Version, nameof(flag)),
                },
                cancellationToken: cancellationToken);
        }

        return ToModel(response);
    }

    private static ProtocolDefinition ToProtocol(AsterloomFeatureDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var result = new ProtocolDefinition
        {
            Enabled = definition.Enabled,
            DefaultVariantKey = definition.DefaultVariantKey,
            BucketingSalt = definition.BucketingSalt,
        };
        result.Variants.AddRange(definition.Variants.Select(variant => new
            Asterloom.Protocol.Feature.V1.FeatureVariant
            {
                Key = variant.Key,
                DisplayName = variant.DisplayName,
                Value = ToProtocol(variant.Value),
            }));
        result.Prerequisites.AddRange(definition.Prerequisites.Select(prerequisite => new
            Asterloom.Protocol.Feature.V1.FeaturePrerequisite
            {
                FlagKey = prerequisite.FlagKey,
                ExpectedVariantKey = prerequisite.ExpectedVariantKey,
            }));
        result.TargetingRules.AddRange(definition.TargetingRules.Select(rule => new
            Asterloom.Protocol.Feature.V1.FeatureTargetingRule
            {
                Id = rule.Id,
                SegmentId = rule.SegmentId.ToString("D"),
                VariantKey = rule.VariantKey,
            }));
        result.Allocations.AddRange(definition.Allocations.Select(allocation => new
            Asterloom.Protocol.Feature.V1.FeatureAllocation
            {
                VariantKey = allocation.VariantKey,
                Start = allocation.Start,
                End = allocation.End,
            }));
        return result;
    }

    private static ProtocolValue ToProtocol(AsterloomFeatureValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Kind switch
        {
            AsterloomFeatureValueKind.Truth =>
                new ProtocolValue { BooleanValue = value.BooleanValue!.Value },
            AsterloomFeatureValueKind.Text =>
                new ProtocolValue { StringValue = value.StringValue! },
            AsterloomFeatureValueKind.WholeNumber =>
                new ProtocolValue { IntegerValue = value.IntegerValue!.Value },
            AsterloomFeatureValueKind.DecimalNumber =>
                new ProtocolValue { DoubleValue = value.DoubleValue!.Value },
            AsterloomFeatureValueKind.Structure =>
                new ProtocolValue { ObjectJson = value.ObjectJson! },
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static AsterloomFeatureFlag ToModel(ProtocolFlag flag) => new(
        ParseId(flag.Id, "flag.id"),
        new AsterloomFeatureScope(
            ParseId(flag.TenantId, "flag.tenant_id"),
            ParseId(flag.ApplicationId, "flag.application_id"),
            ParseId(flag.EnvironmentId, "flag.environment_id")),
        flag.Key,
        flag.DisplayName,
        flag.Description,
        ToModel(flag.ValueKind),
        flag.Status switch
        {
            ProtocolResourceStatus.Active => AsterloomFeatureResourceStatus.Active,
            ProtocolResourceStatus.Archived => AsterloomFeatureResourceStatus.Archived,
            _ => throw InvalidProtocol("flag status"),
        },
        ToModel(flag.DraftDefinition ?? throw InvalidProtocol("draft definition")),
        flag.DraftRevision,
        flag.PublishedDefinition is null ? null : ToModel(flag.PublishedDefinition),
        flag.PublishedRevision <= 0 ? null : flag.PublishedRevision,
        flag.Version,
        ToDateTimeOffset(flag.CreatedAt, "flag.created_at"),
        ToDateTimeOffset(flag.UpdatedAt, "flag.updated_at"),
        flag.ArchivedAt?.ToDateTimeOffset(),
        flag.PublishedAt?.ToDateTimeOffset());

    private static AsterloomFeatureRevision ToModel(ProtocolRevision revision) => new(
        ParseId(revision.Id, "revision.id"),
        ParseId(revision.FlagId, "revision.flag_id"),
        revision.Revision,
        ToModel(revision.Definition ?? throw InvalidProtocol("revision definition")),
        revision.SourceRevision <= 0 ? null : revision.SourceRevision,
        ToDateTimeOffset(revision.PublishedAt, "revision.published_at"));

    private static AsterloomFeatureDefinition ToModel(ProtocolDefinition definition) => new(
        definition.Enabled,
        definition.DefaultVariantKey,
        definition.Variants.Select(variant => new AsterloomFeatureVariant(
            variant.Key,
            variant.DisplayName,
            ToModel(variant.Value ?? throw InvalidProtocol("variant value")))).ToArray(),
        definition.Prerequisites.Select(prerequisite => new AsterloomFeaturePrerequisite(
            prerequisite.FlagKey,
            prerequisite.ExpectedVariantKey)).ToArray(),
        definition.TargetingRules.Select(rule => new AsterloomFeatureTargetingRule(
            rule.Id,
            ParseId(rule.SegmentId, "targeting rule segment_id"),
            rule.VariantKey)).ToArray(),
        definition.Allocations.Select(allocation => new AsterloomFeatureAllocation(
            allocation.VariantKey,
            allocation.Start,
            allocation.End)).ToArray(),
        definition.BucketingSalt);

    private static AsterloomFeatureValue ToModel(ProtocolValue value) => value.ValueCase switch
    {
        ProtocolValue.ValueOneofCase.BooleanValue => AsterloomFeatureValue.From(value.BooleanValue),
        ProtocolValue.ValueOneofCase.StringValue => AsterloomFeatureValue.From(value.StringValue),
        ProtocolValue.ValueOneofCase.IntegerValue => AsterloomFeatureValue.From(value.IntegerValue),
        ProtocolValue.ValueOneofCase.DoubleValue => AsterloomFeatureValue.From(value.DoubleValue),
        ProtocolValue.ValueOneofCase.ObjectJson => AsterloomFeatureValue.FromJson(value.ObjectJson),
        _ => throw InvalidProtocol("feature value"),
    };

    private static AsterloomFeatureEvaluationDetails ToModel(ProtocolEvaluation details) => new(
        ParseId(details.FlagId, "evaluation.flag_id"),
        details.FlagKey,
        details.Revision,
        ToModel(details.Value ?? throw InvalidProtocol("evaluation value")),
        details.VariantKey,
        details.Reason switch
        {
            ProtocolEvaluationReason.Disabled => AsterloomFeatureEvaluationReason.Disabled,
            ProtocolEvaluationReason.TargetingMatch =>
                AsterloomFeatureEvaluationReason.TargetingMatch,
            ProtocolEvaluationReason.Split => AsterloomFeatureEvaluationReason.Split,
            ProtocolEvaluationReason.Default => AsterloomFeatureEvaluationReason.Default,
            ProtocolEvaluationReason.PrerequisiteFailed =>
                AsterloomFeatureEvaluationReason.PrerequisiteFailed,
            _ => throw InvalidProtocol("evaluation reason"),
        },
        details.Trace.ToArray(),
        details.BucketEvaluated,
        details.Bucket,
        details.BucketingVersion,
        details.UsedDraft);

    private static ProtocolContext ToProtocol(TargetingEvaluationContext context)
    {
        var result = new ProtocolContext
        {
            TargetingKey = context.TargetingKey,
            UserId = context.UserId ?? string.Empty,
            ClientVersion = context.ClientVersion ?? string.Empty,
            Platform = context.Platform ?? string.Empty,
            Region = context.Region ?? string.Empty,
            Language = context.Language ?? string.Empty,
        };
        result.Attributes.AddRange(context.Attributes.Select(attribute => new
            Asterloom.Protocol.Targeting.V1.TargetingAttribute
            {
                Key = attribute.Key,
                Value = attribute.Value.Kind switch
                {
                    TargetingValueKind.Text => new() { Text = attribute.Value.StringValue! },
                    TargetingValueKind.Truth => new() { Truth = attribute.Value.BooleanValue!.Value },
                    TargetingValueKind.Numeric => new() { Numeric = attribute.Value.NumberValue!.Value },
                    _ => throw new ArgumentOutOfRangeException(nameof(context)),
                },
            }));
        return result;
    }

    private static ProtocolValueKind ToProtocol(AsterloomFeatureValueKind kind) => kind switch
    {
        AsterloomFeatureValueKind.Truth => ProtocolValueKind.Boolean,
        AsterloomFeatureValueKind.Text => ProtocolValueKind.String,
        AsterloomFeatureValueKind.WholeNumber => ProtocolValueKind.Integer,
        AsterloomFeatureValueKind.DecimalNumber => ProtocolValueKind.Double,
        AsterloomFeatureValueKind.Structure => ProtocolValueKind.Object,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static AsterloomFeatureValueKind ToModel(ProtocolValueKind kind) => kind switch
    {
        ProtocolValueKind.Boolean => AsterloomFeatureValueKind.Truth,
        ProtocolValueKind.String => AsterloomFeatureValueKind.Text,
        ProtocolValueKind.Integer => AsterloomFeatureValueKind.WholeNumber,
        ProtocolValueKind.Double => AsterloomFeatureValueKind.DecimalNumber,
        ProtocolValueKind.Object => AsterloomFeatureValueKind.Structure,
        _ => throw InvalidProtocol("feature value kind"),
    };

    private static AsterloomFeatureValidationSeverity ToModel(ProtocolSeverity severity) =>
        severity switch
        {
            ProtocolSeverity.Error => AsterloomFeatureValidationSeverity.Error,
            ProtocolSeverity.Warning => AsterloomFeatureValidationSeverity.Warning,
            _ => throw InvalidProtocol("validation severity"),
        };

    private static void ValidateDefinition(
        AsterloomFeatureDefinition definition,
        AsterloomFeatureValueKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Variants);
        ArgumentNullException.ThrowIfNull(definition.Prerequisites);
        ArgumentNullException.ThrowIfNull(definition.TargetingRules);
        ArgumentNullException.ThrowIfNull(definition.Allocations);
        if (definition.Variants.Count is < 1 or > 50
            || definition.Variants.Any(variant => variant is null
                || variant.Value is null
                || variant.Value.Kind != expectedKind))
        {
            throw new ArgumentException(
                "A definition requires 1-50 variants with values matching the flag kind.",
                nameof(definition));
        }

        var variantKeys = definition.Variants
            .Select(variant => RequireIdentifier(variant.Key, nameof(definition)))
            .ToHashSet(StringComparer.Ordinal);
        if (variantKeys.Count != definition.Variants.Count
            || !variantKeys.Contains(definition.DefaultVariantKey))
        {
            throw new ArgumentException(
                "Variant keys must be unique and include the default variant.",
                nameof(definition));
        }

        foreach (var variant in definition.Variants)
        {
            _ = RequireText(variant.DisplayName, nameof(definition), 200);
        }

        foreach (var prerequisite in definition.Prerequisites)
        {
            ArgumentNullException.ThrowIfNull(prerequisite);
            _ = RequireIdentifier(prerequisite.FlagKey, nameof(definition));
            _ = RequireIdentifier(prerequisite.ExpectedVariantKey, nameof(definition));
        }

        foreach (var rule in definition.TargetingRules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            _ = RequireIdentifier(rule.Id, nameof(definition));
            _ = FormatId(rule.SegmentId, nameof(definition));
            if (!variantKeys.Contains(rule.VariantKey))
            {
                throw new ArgumentException(
                    "Every targeting rule must reference an existing variant.",
                    nameof(definition));
            }
        }

        _ = TargetingContract.SelectBucketAllocation(
            0,
            definition.Allocations.Select(allocation => new TargetingBucketAllocation(
                allocation.VariantKey,
                allocation.Start,
                allocation.End)).ToArray());
        if (definition.Allocations.Any(allocation => !variantKeys.Contains(allocation.VariantKey)))
        {
            throw new ArgumentException(
                "Every allocation must reference an existing variant.",
                nameof(definition));
        }

        if (definition.BucketingSalt.Length is < 1 or > 500
            || definition.BucketingSalt.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Bucketing salt must contain 1-500 non-control characters.",
                nameof(definition));
        }
    }

    private static void ValidateScope(AsterloomFeatureScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _ = FormatId(scope.TenantId, nameof(scope.TenantId));
        _ = FormatId(scope.ApplicationId, nameof(scope.ApplicationId));
        _ = FormatId(scope.EnvironmentId, nameof(scope.EnvironmentId));
    }

    private static int ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                pageSize,
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        return pageSize;
    }

    private static long ValidateVersion(long version, string parameterName)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                version,
                "The revision or resource version must be positive.");
        }

        return version;
    }

    private static string RequireIdentifier(string? value, string parameterName)
    {
        var normalized = RequireText(value, parameterName, 100).ToLowerInvariant();
        if (!char.IsAsciiLetterOrDigit(normalized[0])
            || !char.IsAsciiLetterOrDigit(normalized[^1])
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException(
                "Use lowercase letters, numbers, periods, underscores, or hyphens.",
                parameterName);
        }

        return normalized;
    }

    private static string RequireText(string? value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)
            || normalized.Length > maximumLength
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"A value of 1-{maximumLength} characters without control characters is required.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeOptional(string? value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The value must not exceed {maximumLength} characters or contain control characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string FormatId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The identifier cannot be empty.", parameterName);
        }

        return value.ToString("D");
    }

    private static Guid ParseId(string value, string field) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw InvalidProtocol(field);

    private static DateTimeOffset ToDateTimeOffset(Timestamp? value, string field) =>
        value?.ToDateTimeOffset() ?? throw InvalidProtocol(field);

    private static string? EmptyToNull(string value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static InvalidDataException InvalidProtocol(string field) =>
        new($"The Feature service returned an invalid {field} value.");
}
