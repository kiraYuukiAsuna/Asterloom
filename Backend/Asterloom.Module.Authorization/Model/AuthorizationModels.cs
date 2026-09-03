using Asterloom.Targeting;

namespace Asterloom.Modules.Authorization.Model;

public enum AuthorizationResourceStatus
{
    Active = 1,
    Archived = 2,
}

public enum AuthorizationPolicyEffect
{
    Allow = 1,
    Deny = 2,
}

public enum AuthorizationPolicySubjectType
{
    Actor = 1,
    Role = 2,
    Any = 3,
}

public sealed record AuthorizationScope(
    Guid? TenantId,
    Guid? ApplicationId,
    Guid? EnvironmentId)
{
    public static AuthorizationScope Global { get; } = new(null, null, null);
}

public sealed record PermissionDefinition(
    Guid Id,
    string Key,
    string DisplayName,
    string Description,
    string Module,
    AuthorizationScope Scope,
    bool IsSystem,
    AuthorizationResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record AuthorizationRole(
    Guid Id,
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Permissions,
    bool IsSystem,
    AuthorizationScope Scope,
    AuthorizationResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record AuthorizationRoleBinding(
    Guid Id,
    string ActorId,
    Guid RoleId,
    string RoleKey,
    AuthorizationScope Scope,
    AuthorizationResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record AuthorizationPolicyRule(
    Guid Id,
    string Name,
    AuthorizationPolicyEffect Effect,
    AuthorizationPolicySubjectType SubjectType,
    string Subject,
    AuthorizationScope Scope,
    string Permission,
    string ResourceType,
    string ResourceId,
    TargetingRule? Condition,
    AuthorizationResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record AuthorizationPolicyRevision(
    Guid Id,
    long RevisionNumber,
    string ChangeType,
    string ResourceType,
    string ResourceId,
    string SnapshotHash,
    string ChangeSummary,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record AuthorizationRevisionDraft(
    string ChangeType,
    string ResourceType,
    string ResourceId,
    string SnapshotHash,
    string ChangeSummary,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record AuthorizationDecisionRequest(
    string ActorId,
    AuthorizationScope Scope,
    string Permission,
    IReadOnlyList<string> TrustedRoles,
    string ResourceType = "",
    string ResourceId = "",
    IReadOnlyDictionary<string, TargetingValue>? Attributes = null);

public sealed record AuthorizationDecisionResult(
    bool Allowed,
    string Reason,
    IReadOnlyList<string> MatchedPolicyIds,
    IReadOnlyList<string> MatchedRoleKeys);

public sealed record AuthorizationPageRequest(
    int Offset,
    int PageSize,
    string Query,
    bool IncludeArchived);

public sealed record AuthorizationStorePage<T>(
    IReadOnlyList<T> Items,
    bool HasMore);

public sealed record AuthorizationPolicySnapshot(
    IReadOnlyList<PermissionDefinition> Permissions,
    IReadOnlyList<AuthorizationRole> Roles,
    IReadOnlyList<AuthorizationRoleBinding> Bindings,
    IReadOnlyList<AuthorizationPolicyRule> PolicyRules);

public sealed record AuthorizationScopeFilter(
    Guid? TenantId,
    Guid? ApplicationId);
