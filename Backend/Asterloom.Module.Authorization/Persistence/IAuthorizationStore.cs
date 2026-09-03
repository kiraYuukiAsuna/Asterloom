using Asterloom.Modules.Authorization.Model;

namespace Asterloom.Modules.Authorization.Persistence;

public interface IAuthorizationStore
{
    Task<AuthorizationStorePage<PermissionDefinition>> ListPermissionsAsync(
        AuthorizationPageRequest page,
        AuthorizationScopeFilter scope,
        CancellationToken cancellationToken);

    Task<PermissionDefinition?> GetPermissionAsync(
        Guid permissionId,
        CancellationToken cancellationToken);

    Task<PermissionDefinition?> FindPermissionAsync(
        AuthorizationScope scope,
        string key,
        CancellationToken cancellationToken);

    Task<bool> TryCreatePermissionAsync(
        PermissionDefinition permission,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken);

    Task<bool> TryUpdatePermissionAsync(
        PermissionDefinition permission,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken);

    Task<AuthorizationStorePage<AuthorizationRole>> ListRolesAsync(
        AuthorizationPageRequest page,
        AuthorizationScopeFilter scope,
        CancellationToken cancellationToken);

    Task<AuthorizationRole?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken);

    Task<bool> TryCreateRoleAsync(
        AuthorizationRole role,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateRoleAsync(
        AuthorizationRole role,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken);

    Task<AuthorizationStorePage<AuthorizationRoleBinding>> ListRoleBindingsAsync(
        AuthorizationPageRequest page,
        string actorId,
        AuthorizationScopeFilter scope,
        CancellationToken cancellationToken);

    Task<AuthorizationRoleBinding?> GetRoleBindingAsync(
        Guid bindingId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateRoleBindingAsync(
        AuthorizationRoleBinding binding,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateRoleBindingAsync(
        AuthorizationRoleBinding binding,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken);

    Task<AuthorizationStorePage<AuthorizationPolicyRule>> ListPolicyRulesAsync(
        AuthorizationPageRequest page,
        AuthorizationScopeFilter scope,
        CancellationToken cancellationToken);

    Task<AuthorizationPolicyRule?> GetPolicyRuleAsync(
        Guid policyRuleId,
        CancellationToken cancellationToken);

    Task<bool> TryCreatePolicyRuleAsync(
        AuthorizationPolicyRule policyRule,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken);

    Task<bool> TryUpdatePolicyRuleAsync(
        AuthorizationPolicyRule policyRule,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken);

    Task<AuthorizationStorePage<AuthorizationPolicyRevision>> ListPolicyRevisionsAsync(
        AuthorizationPageRequest page,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken);

    Task<AuthorizationPolicySnapshot> GetPolicySnapshotAsync(
        CancellationToken cancellationToken);
}
