using Asterloom.Protocol.Targeting.Admin.V1;
using Asterloom.Targeting;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProtocolAllocation = Asterloom.Protocol.Targeting.V1.BucketAllocation;
using ProtocolCondition = Asterloom.Protocol.Targeting.V1.TargetingCondition;
using ProtocolConditionReason = Asterloom.Protocol.Targeting.V1.TargetingConditionReason;
using ProtocolContext = Asterloom.Protocol.Targeting.V1.EvaluationContext;
using ProtocolMatchMode = Asterloom.Protocol.Targeting.V1.TargetingMatchMode;
using ProtocolOperator = Asterloom.Protocol.Targeting.V1.TargetingOperator;
using ProtocolRule = Asterloom.Protocol.Targeting.V1.TargetingRule;
using ProtocolSegment = Asterloom.Protocol.Targeting.V1.Segment;
using ProtocolStatus = Asterloom.Protocol.Targeting.V1.TargetingResourceStatus;
using ProtocolValue = Asterloom.Protocol.Targeting.V1.TargetingValue;
using ProtocolValueKind = Asterloom.Protocol.Targeting.V1.TargetingValueKind;

namespace Asterloom.Sdk.Targeting;

public sealed class AsterloomTargetingAdminClient
{
    private const int MaximumPageSize = 100;

    private readonly TargetingAdminService.TargetingAdminServiceClient _client;

    public AsterloomTargetingAdminClient(CallInvoker callInvoker)
        : this(new TargetingAdminService.TargetingAdminServiceClient(
            callInvoker ?? throw new ArgumentNullException(nameof(callInvoker))))
    {
    }

    public AsterloomTargetingAdminClient(
        TargetingAdminService.TargetingAdminServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<AsterloomTargetingCatalog> ListTargetingAttributesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _client.ListTargetingAttributesAsync(
            new Empty(),
            cancellationToken: cancellationToken);
        return new AsterloomTargetingCatalog(
            response.Attributes.Select(attribute => new AsterloomTargetingAttributeDefinition(
                attribute.Key,
                attribute.DisplayName,
                ToModel(attribute.ValueKind),
                attribute.BuiltIn,
                attribute.Required)).ToArray(),
            response.Operators.Select(metadata => new AsterloomTargetingOperatorDefinition(
                ToModel(metadata.Operator),
                metadata.DisplayName,
                metadata.SupportedValueKinds.Select(ToModel).ToArray(),
                metadata.MinimumValues,
                metadata.MaximumValues)).ToArray(),
            response.MaximumCustomAttributes,
            response.MaximumConditions,
            response.BucketingVersion,
            response.BucketCount);
    }

    public async Task<AsterloomTargetingPage<AsterloomTargetingSegment>> ListSegmentsAsync(
        AsterloomTargetingScope scope,
        string? query = null,
        bool includeArchived = false,
        int pageSize = MaximumPageSize,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        var response = await _client.ListSegmentsAsync(
            new ListSegmentsRequest
            {
                TenantId = FormatId(scope.TenantId, nameof(scope.TenantId)),
                ApplicationId = FormatId(scope.ApplicationId, nameof(scope.ApplicationId)),
                EnvironmentId = FormatId(scope.EnvironmentId, nameof(scope.EnvironmentId)),
                Query = NormalizeOptional(query, 200),
                IncludeArchived = includeArchived,
                PageSize = ValidatePageSize(pageSize),
                PageToken = NormalizeOptional(pageToken, 2_048),
            },
            cancellationToken: cancellationToken);
        return new AsterloomTargetingPage<AsterloomTargetingSegment>(
            response.Segments.Select(ToModel).ToArray(),
            EmptyToNull(response.NextPageToken));
    }

    public async Task<AsterloomTargetingSegment> GetSegmentAsync(
        AsterloomTargetingScope scope,
        Guid segmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        return ToModel(await _client.GetSegmentAsync(
            new GetSegmentRequest
            {
                TenantId = scope.TenantId.ToString("D"),
                ApplicationId = scope.ApplicationId.ToString("D"),
                EnvironmentId = scope.EnvironmentId.ToString("D"),
                SegmentId = FormatId(segmentId, nameof(segmentId)),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomTargetingSegment> CreateSegmentAsync(
        AsterloomTargetingScope scope,
        AsterloomTargetingSegmentRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        ArgumentNullException.ThrowIfNull(registration);
        var key = NormalizeKey(registration.Key, scope.EnvironmentId);
        var request = new CreateSegmentRequest
        {
            TenantId = scope.TenantId.ToString("D"),
            ApplicationId = scope.ApplicationId.ToString("D"),
            EnvironmentId = scope.EnvironmentId.ToString("D"),
            Key = key,
            DisplayName = RequireText(registration.DisplayName, nameof(registration), 200),
            Description = NormalizeOptional(registration.Description, 1_000),
            Rule = ToProtocol(registration.Rule),
        };
        return ToModel(await _client.CreateSegmentAsync(
            request,
            cancellationToken: cancellationToken));
    }

    public async Task<AsterloomTargetingSegment> UpdateSegmentAsync(
        AsterloomTargetingSegment segment,
        AsterloomTargetingSegmentUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(update);
        ValidateScope(segment.Scope);
        var request = new UpdateSegmentRequest
        {
            TenantId = segment.Scope.TenantId.ToString("D"),
            ApplicationId = segment.Scope.ApplicationId.ToString("D"),
            EnvironmentId = segment.Scope.EnvironmentId.ToString("D"),
            SegmentId = FormatId(segment.Id, nameof(segment)),
            DisplayName = RequireText(update.DisplayName, nameof(update), 200),
            Description = NormalizeOptional(update.Description, 1_000),
            Rule = ToProtocol(update.Rule),
            ExpectedVersion = ValidateVersion(segment.Version, nameof(segment)),
        };
        return ToModel(await _client.UpdateSegmentAsync(
            request,
            cancellationToken: cancellationToken));
    }

    public Task<AsterloomTargetingSegment> ArchiveSegmentAsync(
        AsterloomTargetingSegment segment,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(segment, restore: false, cancellationToken);

    public Task<AsterloomTargetingSegment> RestoreSegmentAsync(
        AsterloomTargetingSegment segment,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(segment, restore: true, cancellationToken);

    public Task<AsterloomTargetingSimulationResult> SimulateAsync(
        AsterloomTargetingSegment segment,
        TargetingEvaluationContext context,
        AsterloomTargetingBucketPreview? bucketPreview = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return SimulateAsync(
            segment.Scope,
            segment.Id,
            context,
            bucketPreview,
            cancellationToken);
    }

    public async Task<AsterloomTargetingSimulationResult> SimulateAsync(
        AsterloomTargetingScope scope,
        Guid segmentId,
        TargetingEvaluationContext context,
        AsterloomTargetingBucketPreview? bucketPreview = null,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        ArgumentNullException.ThrowIfNull(context);
        if (context.ApplicationId != scope.ApplicationId
            || context.EnvironmentId != scope.EnvironmentId)
        {
            throw new ArgumentException(
                "The context application and environment must match the requested scope.",
                nameof(context));
        }

        TargetingContract.ValidateContext(context);
        var request = new SimulateTargetingRequest
        {
            TenantId = scope.TenantId.ToString("D"),
            ApplicationId = scope.ApplicationId.ToString("D"),
            EnvironmentId = scope.EnvironmentId.ToString("D"),
            SegmentId = FormatId(segmentId, nameof(segmentId)),
            Context = ToProtocol(context),
            BucketPreview = bucketPreview is null ? null : ToProtocol(bucketPreview),
        };
        var response = await _client.SimulateTargetingAsync(
            request,
            cancellationToken: cancellationToken);
        return new AsterloomTargetingSimulationResult(
            ParseId(response.SegmentId, "simulation.segment_id"),
            response.SegmentKey,
            response.SegmentVersion,
            response.Matched,
            response.Reason,
            response.ConditionTraces.Select(trace => new TargetingConditionResult(
                trace.ConditionId,
                trace.Matched,
                ToModel(trace.Reason))).ToArray(),
            response.BucketEvaluated,
            response.Bucket,
            EmptyToNull(response.SelectedVariant),
            EmptyToNull(response.BucketNamespace),
            response.BucketingVersion);
    }

    private async Task<AsterloomTargetingSegment> ChangeStatusAsync(
        AsterloomTargetingSegment segment,
        bool restore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ValidateScope(segment.Scope);
        var version = ValidateVersion(segment.Version, nameof(segment));
        ProtocolSegment response;
        if (restore)
        {
            response = await _client.RestoreSegmentAsync(
                new RestoreSegmentRequest
                {
                    TenantId = segment.Scope.TenantId.ToString("D"),
                    ApplicationId = segment.Scope.ApplicationId.ToString("D"),
                    EnvironmentId = segment.Scope.EnvironmentId.ToString("D"),
                    SegmentId = FormatId(segment.Id, nameof(segment)),
                    ExpectedVersion = version,
                },
                cancellationToken: cancellationToken);
        }
        else
        {
            response = await _client.ArchiveSegmentAsync(
                new ArchiveSegmentRequest
                {
                    TenantId = segment.Scope.TenantId.ToString("D"),
                    ApplicationId = segment.Scope.ApplicationId.ToString("D"),
                    EnvironmentId = segment.Scope.EnvironmentId.ToString("D"),
                    SegmentId = FormatId(segment.Id, nameof(segment)),
                    ExpectedVersion = version,
                },
                cancellationToken: cancellationToken);
        }

        return ToModel(response);
    }

    private static AsterloomTargetingSegment ToModel(ProtocolSegment segment)
    {
        var rule = ToModel(segment.Rule ?? throw InvalidProtocol("segment rule"));
        TargetingContract.ValidateRule(rule);
        return new AsterloomTargetingSegment(
            ParseId(segment.Id, "segment.id"),
            new AsterloomTargetingScope(
                ParseId(segment.TenantId, "segment.tenant_id"),
                ParseId(segment.ApplicationId, "segment.application_id"),
                ParseId(segment.EnvironmentId, "segment.environment_id")),
            segment.Key,
            segment.DisplayName,
            segment.Description,
            rule,
            segment.Status switch
            {
                ProtocolStatus.Active => AsterloomTargetingResourceStatus.Active,
                ProtocolStatus.Archived => AsterloomTargetingResourceStatus.Archived,
                _ => throw InvalidProtocol("segment status"),
            },
            segment.Version,
            ToDateTimeOffset(segment.CreatedAt, "segment.created_at"),
            ToDateTimeOffset(segment.UpdatedAt, "segment.updated_at"),
            segment.ArchivedAt?.ToDateTimeOffset());
    }

    private static ProtocolRule ToProtocol(TargetingRule rule)
    {
        TargetingContract.ValidateRule(rule);
        var result = new ProtocolRule
        {
            MatchMode = rule.MatchMode switch
            {
                TargetingMatchMode.All => ProtocolMatchMode.All,
                TargetingMatchMode.Any => ProtocolMatchMode.Any,
                _ => throw new ArgumentOutOfRangeException(nameof(rule)),
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
            ValueKind = (ProtocolValueKind)(int)condition.ValueKind,
            Operator = (ProtocolOperator)(int)condition.Operator,
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
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static TargetingRule ToModel(ProtocolRule rule) => new(
        rule.MatchMode switch
        {
            ProtocolMatchMode.All => TargetingMatchMode.All,
            ProtocolMatchMode.Any => TargetingMatchMode.Any,
            _ => throw InvalidProtocol("targeting match mode"),
        },
        rule.Conditions.Select(condition => new TargetingCondition(
            condition.Id,
            condition.Attribute,
            ToModel(condition.ValueKind),
            ToModel(condition.Operator),
            condition.Values.Select(ToModel).ToArray(),
            condition.CaseSensitive)).ToArray());

    private static TargetingValue ToModel(ProtocolValue value) => value.ValueCase switch
    {
        ProtocolValue.ValueOneofCase.Text => TargetingValue.From(value.Text),
        ProtocolValue.ValueOneofCase.Truth => TargetingValue.From(value.Truth),
        ProtocolValue.ValueOneofCase.Numeric => TargetingValue.From(value.Numeric),
        _ => throw InvalidProtocol("targeting value"),
    };

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
                Value = ToProtocol(attribute.Value),
            }));
        return result;
    }

    private static Asterloom.Protocol.Targeting.V1.BucketPreview ToProtocol(
        AsterloomTargetingBucketPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview.Allocations);
        var bucketNamespace = TargetingContract.CreateBucketNamespace(
            preview.ResourceType,
            preview.ResourceKey,
            Guid.Parse("11111111-1111-7111-8111-111111111111"));
        _ = TargetingContract.ComputeBucket(bucketNamespace, preview.Salt, "validation");
        _ = TargetingContract.SelectBucketAllocation(0, preview.Allocations);
        var result = new Asterloom.Protocol.Targeting.V1.BucketPreview
        {
            ResourceType = preview.ResourceType,
            ResourceKey = preview.ResourceKey,
            Salt = preview.Salt,
        };
        result.Allocations.AddRange(preview.Allocations.Select(allocation => new ProtocolAllocation
        {
            Variant = allocation.Variant,
            Start = allocation.Start,
            End = allocation.End,
        }));
        return result;
    }

    private static TargetingValueKind ToModel(ProtocolValueKind kind) => kind switch
    {
        ProtocolValueKind.Text => TargetingValueKind.Text,
        ProtocolValueKind.Truth => TargetingValueKind.Truth,
        ProtocolValueKind.Numeric => TargetingValueKind.Numeric,
        _ => throw InvalidProtocol("targeting value kind"),
    };

    private static TargetingOperator ToModel(ProtocolOperator value) =>
        System.Enum.IsDefined(typeof(TargetingOperator), (int)value)
            && value != ProtocolOperator.Unspecified
            ? (TargetingOperator)(int)value
            : throw InvalidProtocol("targeting operator");

    private static TargetingConditionReason ToModel(ProtocolConditionReason value) =>
        System.Enum.IsDefined(typeof(TargetingConditionReason), (int)value)
            && value != ProtocolConditionReason.Unspecified
            ? (TargetingConditionReason)(int)value
            : throw InvalidProtocol("targeting condition reason");

    private static string NormalizeKey(string value, Guid environmentId)
    {
        var normalized = RequireText(value, nameof(value), 100).ToLowerInvariant();
        _ = TargetingContract.CreateBucketNamespace("segment", normalized, environmentId);
        return normalized;
    }

    private static void ValidateScope(AsterloomTargetingScope scope)
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
                "The resource version must be positive.");
        }

        return version;
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
        new($"The Targeting service returned an invalid {field} value.");
}
