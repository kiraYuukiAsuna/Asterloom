using Asterloom.Protocol.Authorization.V1;
using Grpc.Core;

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
        var response = await _client.CheckPermissionAsync(
            new AuthorizationDecisionInput
            {
                Permission = permission.Trim(),
                Scope = new AuthorizationScope
                {
                    TenantId = normalizedScope.TenantId?.ToString("D") ?? string.Empty,
                    ApplicationId = normalizedScope.ApplicationId?.ToString("D")
                        ?? string.Empty,
                    EnvironmentId = normalizedScope.EnvironmentId?.ToString("D")
                        ?? string.Empty,
                },
            },
            cancellationToken: cancellationToken);

        return new AsterloomAuthorizationDecision(
            response.Allowed,
            response.Reason,
            response.MatchedPolicyIds.ToArray(),
            response.MatchedRoleKeys.ToArray());
    }

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
