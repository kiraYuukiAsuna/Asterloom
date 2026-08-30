using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Errors;
using Google.Protobuf.WellKnownTypes;
using ProtocolDecision = Asterloom.Protocol.Authorization.V1.AuthorizationDecision;
using ProtocolEffect = Asterloom.Protocol.Authorization.V1.PolicyEffect;
using ProtocolPermission = Asterloom.Protocol.Authorization.V1.PermissionDefinition;
using ProtocolPolicyRule = Asterloom.Protocol.Authorization.V1.PolicyRule;
using ProtocolRevision = Asterloom.Protocol.Authorization.V1.PolicyRevision;
using ProtocolRole = Asterloom.Protocol.Authorization.V1.Role;
using ProtocolRoleBinding = Asterloom.Protocol.Authorization.V1.RoleBinding;
using ProtocolScope = Asterloom.Protocol.Authorization.V1.AuthorizationScope;
using ProtocolStatus = Asterloom.Protocol.Authorization.V1.AuthorizationResourceStatus;
using ProtocolSubjectType = Asterloom.Protocol.Authorization.V1.PolicySubjectType;

namespace Asterloom.Modules.Authorization;

internal static class AuthorizationProtocolMapper
{
    public static ProtocolPermission ToProtocol(this PermissionDefinition permission) => new()
    {
        Key = permission.Key,
        DisplayName = permission.DisplayName,
        Description = permission.Description,
        Module = permission.Module,
    };

    public static ProtocolRole ToProtocol(this AuthorizationRole role)
    {
        var result = new ProtocolRole
        {
            Id = role.Id.ToString("D"),
            Key = role.Key,
            DisplayName = role.DisplayName,
            Description = role.Description,
            IsSystem = role.IsSystem,
            Status = role.Status.ToProtocol(),
            Version = role.Version,
            CreatedAt = Timestamp.FromDateTimeOffset(role.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(role.UpdatedAt),
            ArchivedAt = role.ArchivedAt is { } archivedAt
                ? Timestamp.FromDateTimeOffset(archivedAt)
                : null,
        };
        result.Permissions.AddRange(role.Permissions);
        return result;
    }

    public static ProtocolRoleBinding ToProtocol(this AuthorizationRoleBinding binding) => new()
    {
        Id = binding.Id.ToString("D"),
        ActorId = binding.ActorId,
        RoleId = binding.RoleId.ToString("D"),
        RoleKey = binding.RoleKey,
        Scope = binding.Scope.ToProtocol(),
        Status = binding.Status.ToProtocol(),
        Version = binding.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(binding.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(binding.UpdatedAt),
        ArchivedAt = binding.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolPolicyRule ToProtocol(this AuthorizationPolicyRule policyRule) => new()
    {
        Id = policyRule.Id.ToString("D"),
        Name = policyRule.Name,
        Effect = policyRule.Effect.ToProtocol(),
        SubjectType = policyRule.SubjectType.ToProtocol(),
        Subject = policyRule.Subject,
        Scope = policyRule.Scope.ToProtocol(),
        Permission = policyRule.Permission,
        Status = policyRule.Status.ToProtocol(),
        Version = policyRule.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(policyRule.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(policyRule.UpdatedAt),
        ArchivedAt = policyRule.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolRevision ToProtocol(this AuthorizationPolicyRevision revision) => new()
    {
        Id = revision.Id.ToString("D"),
        RevisionNumber = revision.RevisionNumber,
        ChangeType = revision.ChangeType,
        ResourceType = revision.ResourceType,
        ResourceId = revision.ResourceId,
        SnapshotHash = revision.SnapshotHash,
        ChangeSummary = revision.ChangeSummary,
        CreatedBy = revision.CreatedBy,
        CreatedAt = Timestamp.FromDateTimeOffset(revision.CreatedAt),
    };

    public static ProtocolDecision ToProtocol(this AuthorizationDecisionResult decision)
    {
        var result = new ProtocolDecision
        {
            Allowed = decision.Allowed,
            Reason = decision.Reason,
        };
        result.MatchedPolicyIds.AddRange(decision.MatchedPolicyIds);
        result.MatchedRoleKeys.AddRange(decision.MatchedRoleKeys);
        return result;
    }

    public static AuthorizationDecisionRequest ToDomain(
        this Asterloom.Protocol.Authorization.V1.AuthorizationDecisionInput input) =>
        new(
            input.ActorId,
            input.Scope.ToDomain(),
            input.Permission,
            input.TrustedRoles.ToArray());

    public static AuthorizationScope ToDomain(this ProtocolScope? scope) => scope is null
        ? AuthorizationScope.Global
        : new AuthorizationScope(
            ParseOptionalId(scope.TenantId, "scope.tenantId"),
            ParseOptionalId(scope.ApplicationId, "scope.applicationId"),
            ParseOptionalId(scope.EnvironmentId, "scope.environmentId"));

    public static AuthorizationPolicyEffect ToDomain(this ProtocolEffect effect) => effect switch
    {
        ProtocolEffect.Allow => AuthorizationPolicyEffect.Allow,
        ProtocolEffect.Deny => AuthorizationPolicyEffect.Deny,
        _ => (AuthorizationPolicyEffect)0,
    };

    public static AuthorizationPolicySubjectType ToDomain(
        this ProtocolSubjectType subjectType) => subjectType switch
        {
            ProtocolSubjectType.Actor => AuthorizationPolicySubjectType.Actor,
            ProtocolSubjectType.Role => AuthorizationPolicySubjectType.Role,
            ProtocolSubjectType.Any => AuthorizationPolicySubjectType.Any,
            _ => (AuthorizationPolicySubjectType)0,
        };

    private static ProtocolScope ToProtocol(this AuthorizationScope scope) => new()
    {
        TenantId = scope.TenantId?.ToString("D") ?? string.Empty,
        ApplicationId = scope.ApplicationId?.ToString("D") ?? string.Empty,
        EnvironmentId = scope.EnvironmentId?.ToString("D") ?? string.Empty,
    };

    private static ProtocolStatus ToProtocol(this AuthorizationResourceStatus status) =>
        status switch
        {
            AuthorizationResourceStatus.Active => ProtocolStatus.Active,
            AuthorizationResourceStatus.Archived => ProtocolStatus.Archived,
            _ => ProtocolStatus.Unspecified,
        };

    private static ProtocolEffect ToProtocol(this AuthorizationPolicyEffect effect) => effect switch
    {
        AuthorizationPolicyEffect.Allow => ProtocolEffect.Allow,
        AuthorizationPolicyEffect.Deny => ProtocolEffect.Deny,
        _ => ProtocolEffect.Unspecified,
    };

    private static ProtocolSubjectType ToProtocol(
        this AuthorizationPolicySubjectType subjectType) => subjectType switch
        {
            AuthorizationPolicySubjectType.Actor => ProtocolSubjectType.Actor,
            AuthorizationPolicySubjectType.Role => ProtocolSubjectType.Role,
            AuthorizationPolicySubjectType.Any => ProtocolSubjectType.Any,
            _ => ProtocolSubjectType.Unspecified,
        };

    private static Guid? ParseOptionalId(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            return id;
        }

        throw new AsterloomException(
            AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [field] = ["A valid identifier is required."],
            });
    }
}
