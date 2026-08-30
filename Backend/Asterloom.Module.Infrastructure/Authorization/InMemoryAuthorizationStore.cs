using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;

namespace Asterloom.Modules.Infrastructure.Authorization;

internal sealed class InMemoryAuthorizationStore : IAuthorizationStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, AuthorizationRole> _roles = [];
    private readonly Dictionary<Guid, AuthorizationRoleBinding> _bindings = [];
    private readonly Dictionary<Guid, AuthorizationPolicyRule> _policyRules = [];
    private readonly List<AuthorizationPolicyRevision> _revisions = [];
    private long _revisionNumber;

    public Task<AuthorizationStorePage<AuthorizationRole>> ListRolesAsync(
        AuthorizationPageRequest page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Page(
                _roles.Values
                    .Where(role => page.IncludeArchived
                        || role.Status == AuthorizationResourceStatus.Active)
                    .Where(role => Matches(role.Key, role.DisplayName, page.Query))
                    .OrderBy(static role => role.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static role => role.Id),
                page));
        }
    }

    public Task<AuthorizationRole?> GetRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_roles.GetValueOrDefault(roleId));
        }
    }

    public Task<bool> TryCreateRoleAsync(
        AuthorizationRole role,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_roles.ContainsKey(role.Id)
                || _roles.Values.Any(candidate => string.Equals(
                    candidate.Key,
                    role.Key,
                    StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            _roles.Add(role.Id, role);
            AddRevision(revision);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateRoleAsync(
        AuthorizationRole role,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_roles.TryGetValue(role.Id, out var current)
                || current.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }

            _roles[role.Id] = role;
            AddRevision(revision);
            return Task.FromResult(true);
        }
    }

    public Task<AuthorizationStorePage<AuthorizationRoleBinding>> ListRoleBindingsAsync(
        AuthorizationPageRequest page,
        string actorId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Page(
                _bindings.Values
                    .Where(binding => page.IncludeArchived
                        || binding.Status == AuthorizationResourceStatus.Active)
                    .Where(binding => string.IsNullOrEmpty(actorId)
                        || binding.ActorId.Contains(actorId, StringComparison.OrdinalIgnoreCase))
                    .Where(binding => tenantId is null || binding.Scope.TenantId == tenantId)
                    .OrderBy(static binding => binding.ActorId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static binding => binding.Id),
                page));
        }
    }

    public Task<AuthorizationRoleBinding?> GetRoleBindingAsync(
        Guid bindingId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_bindings.GetValueOrDefault(bindingId));
        }
    }

    public Task<bool> TryCreateRoleBindingAsync(
        AuthorizationRoleBinding binding,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_bindings.ContainsKey(binding.Id)
                || _bindings.Values.Any(candidate => SameBinding(candidate, binding)))
            {
                return Task.FromResult(false);
            }

            _bindings.Add(binding.Id, binding);
            AddRevision(revision);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateRoleBindingAsync(
        AuthorizationRoleBinding binding,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_bindings.TryGetValue(binding.Id, out var current)
                || current.Version != expectedVersion
                || _bindings.Values.Any(candidate =>
                    candidate.Id != binding.Id && SameBinding(candidate, binding)))
            {
                return Task.FromResult(false);
            }

            _bindings[binding.Id] = binding;
            AddRevision(revision);
            return Task.FromResult(true);
        }
    }

    public Task<AuthorizationStorePage<AuthorizationPolicyRule>> ListPolicyRulesAsync(
        AuthorizationPageRequest page,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Page(
                _policyRules.Values
                    .Where(rule => page.IncludeArchived
                        || rule.Status == AuthorizationResourceStatus.Active)
                    .Where(rule => tenantId is null || rule.Scope.TenantId == tenantId)
                    .Where(rule => Matches(rule.Name, rule.Permission, page.Query))
                    .OrderBy(static rule => rule.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static rule => rule.Id),
                page));
        }
    }

    public Task<AuthorizationPolicyRule?> GetPolicyRuleAsync(
        Guid policyRuleId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_policyRules.GetValueOrDefault(policyRuleId));
        }
    }

    public Task<bool> TryCreatePolicyRuleAsync(
        AuthorizationPolicyRule policyRule,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_policyRules.ContainsKey(policyRule.Id))
            {
                return Task.FromResult(false);
            }

            _policyRules.Add(policyRule.Id, policyRule);
            AddRevision(revision);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdatePolicyRuleAsync(
        AuthorizationPolicyRule policyRule,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_policyRules.TryGetValue(policyRule.Id, out var current)
                || current.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }

            _policyRules[policyRule.Id] = policyRule;
            AddRevision(revision);
            return Task.FromResult(true);
        }
    }

    public Task<AuthorizationStorePage<AuthorizationPolicyRevision>>
        ListPolicyRevisionsAsync(
            AuthorizationPageRequest page,
            string resourceType,
            string resourceId,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Page(
                _revisions
                    .Where(revision => string.IsNullOrEmpty(resourceType)
                        || string.Equals(
                            revision.ResourceType,
                            resourceType,
                            StringComparison.OrdinalIgnoreCase))
                    .Where(revision => string.IsNullOrEmpty(resourceId)
                        || string.Equals(
                            revision.ResourceId,
                            resourceId,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(static revision => revision.RevisionNumber),
                page));
        }
    }

    public Task<AuthorizationPolicySnapshot> GetPolicySnapshotAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(new AuthorizationPolicySnapshot(
                _roles.Values
                    .Where(static role => role.Status == AuthorizationResourceStatus.Active)
                    .ToArray(),
                _bindings.Values
                    .Where(static binding =>
                        binding.Status == AuthorizationResourceStatus.Active)
                    .ToArray(),
                _policyRules.Values
                    .Where(static policyRule =>
                        policyRule.Status == AuthorizationResourceStatus.Active)
                    .ToArray()));
        }
    }

    private void AddRevision(AuthorizationRevisionDraft draft)
    {
        _revisions.Add(new AuthorizationPolicyRevision(
            Guid.CreateVersion7(),
            ++_revisionNumber,
            draft.ChangeType,
            draft.ResourceType,
            draft.ResourceId,
            draft.SnapshotHash,
            draft.ChangeSummary,
            draft.CreatedBy,
            draft.CreatedAt));
    }

    private static bool SameBinding(
        AuthorizationRoleBinding left,
        AuthorizationRoleBinding right) =>
        string.Equals(left.ActorId, right.ActorId, StringComparison.Ordinal)
        && left.RoleId == right.RoleId
        && left.Scope == right.Scope;

    private static bool Matches(string first, string second, string query) =>
        string.IsNullOrEmpty(query)
        || first.Contains(query, StringComparison.OrdinalIgnoreCase)
        || second.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static AuthorizationStorePage<T> Page<T>(
        IEnumerable<T> source,
        AuthorizationPageRequest page)
    {
        var items = source.Skip(page.Offset).Take(page.PageSize + 1).ToArray();
        return new AuthorizationStorePage<T>(
            items.Take(page.PageSize).ToArray(),
            items.Length > page.PageSize);
    }
}
