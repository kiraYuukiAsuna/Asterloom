using Asterloom.Protocol.Authorization.V1;
using Asterloom.Targeting;
using Grpc.Core;
using ProtocolAttribute = Asterloom.Protocol.Targeting.V1.TargetingAttribute;
using ProtocolValue = Asterloom.Protocol.Targeting.V1.TargetingValue;

namespace Asterloom.Sdk.Authorization;

public sealed record AsterloomAuthorizationScope(
    Guid? TenantId = null,
    Guid? ApplicationId = null,
    Guid? EnvironmentId = null)
{
    public static AsterloomAuthorizationScope Global { get; } = new();
}

public sealed record AsterloomAuthorizationDecision(
    bool Allowed,
    string Reason,
    IReadOnlyList<string> MatchedPolicyIds,
    IReadOnlyList<string> MatchedRoleKeys);

public sealed class AsterloomAuthorizationClient
{
    private readonly AuthorizationService.AuthorizationServiceClient _client;

    public AsterloomAuthorizationClient(CallInvoker callInvoker)
        : this(new AuthorizationService.AuthorizationServiceClient(
            callInvoker ?? throw new ArgumentNullException(nameof(callInvoker))))
    {
    }

    public AsterloomAuthorizationClient(
        AuthorizationService.AuthorizationServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<AsterloomAuthorizationDecision> CheckPermissionAsync(
        string permission,
        AsterloomAuthorizationScope? scope = null,
        CancellationToken cancellationToken = default)
    {
        return await CheckAccessAsync(
            actorId: null,
            permission,
            scope,
            resourceType: null,
            resourceId: null,
            attributes: null,
            cancellationToken);
    }

    public async Task<AsterloomAuthorizationDecision> CheckAccessAsync(
        string? actorId,
        string permission,
        AsterloomAuthorizationScope? scope = null,
        string? resourceType = null,
        string? resourceId = null,
        IReadOnlyDictionary<string, TargetingValue>? attributes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        if (permission.Length > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(permission),
                permission.Length,
                "Permission keys cannot exceed 200 characters.");
        }

        var normalizedScope = scope ?? AsterloomAuthorizationScope.Global;
        ValidateScope(normalizedScope);
        var normalizedResourceType = resourceType?.Trim() ?? string.Empty;
        var normalizedResourceId = resourceId?.Trim() ?? string.Empty;
        if (normalizedResourceType.Length > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resourceType),
                "Resource type cannot exceed 100 characters.");
        }

        if (normalizedResourceId.Length > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resourceId),
                "Resource ID cannot exceed 500 characters.");
        }

        if (normalizedResourceId.Length > 0 && normalizedResourceType.Length == 0)
        {
            throw new ArgumentException(
                "Resource type is required when resource ID is set.",
                nameof(resourceType));
        }

        if ((attributes?.Count ?? 0) > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attributes),
                "At most 64 authorization attributes are accepted.");
        }

        var request = new AuthorizationDecisionInput
        {
            ActorId = actorId?.Trim() ?? string.Empty,
            Permission = permission.Trim(),
            ResourceType = normalizedResourceType,
            ResourceId = normalizedResourceId,
            Scope = new AuthorizationScope
            {
                TenantId = normalizedScope.TenantId?.ToString("D") ?? string.Empty,
                ApplicationId = normalizedScope.ApplicationId?.ToString("D")
                    ?? string.Empty,
                EnvironmentId = normalizedScope.EnvironmentId?.ToString("D")
                    ?? string.Empty,
            },
        };
        if (attributes is not null)
        {
            request.Attributes.AddRange(attributes.Select(attribute => new ProtocolAttribute
            {
                Key = attribute.Key,
                Value = ToProtocol(attribute.Value),
            }));
        }

        var response = await _client.CheckPermissionAsync(
            request,
            cancellationToken: cancellationToken);

        return new AsterloomAuthorizationDecision(
            response.Allowed,
            response.Reason,
            response.MatchedPolicyIds.ToArray(),
            response.MatchedRoleKeys.ToArray());
    }

    private static ProtocolValue ToProtocol(TargetingValue value) => value.Kind switch
    {
        TargetingValueKind.Text => new ProtocolValue { Text = value.StringValue! },
        TargetingValueKind.Truth => new ProtocolValue { Truth = value.BooleanValue!.Value },
        TargetingValueKind.Numeric => new ProtocolValue { Numeric = value.NumberValue!.Value },
        _ => throw new ArgumentException("Unsupported authorization attribute value.", nameof(value)),
    };

    private static void ValidateScope(AsterloomAuthorizationScope scope)
    {
        if (scope.EnvironmentId is not null && scope.ApplicationId is null)
        {
            throw new ArgumentException(
                "ApplicationId is required when EnvironmentId is set.",
                nameof(scope));
        }

        if (scope.ApplicationId is not null && scope.TenantId is null)
        {
            throw new ArgumentException(
                "TenantId is required when ApplicationId is set.",
                nameof(scope));
        }
    }
}
