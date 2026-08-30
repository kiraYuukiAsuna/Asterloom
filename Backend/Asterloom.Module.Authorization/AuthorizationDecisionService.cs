using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Casbin;
using Casbin.Model;

namespace Asterloom.Modules.Authorization;

public sealed class AuthorizationDecisionService(IAuthorizationStore store)
{
    private const string CasbinModel =
        """
        [request_definition]
        r = sub, tenant, application, environment, permission

        [policy_definition]
        p = sub, tenant, application, environment, permission, eft

        [policy_effect]
        e = some(where (p.eft == allow)) && !some(where (p.eft == deny))

        [matchers]
        m = r.sub == p.sub && \
            (p.tenant == "*" || r.tenant == p.tenant) && \
            (p.application == "*" || r.application == p.application) && \
            (p.environment == "*" || r.environment == p.environment) && \
            (p.permission == "*" || r.permission == p.permission)
        """;

    public async Task<AuthorizationDecisionResult> DecideAsync(
        AuthorizationDecisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = await store.GetPolicySnapshotAsync(cancellationToken);
        var roles = AuthorizationCatalog.SystemRoles
            .Concat(snapshot.Roles)
            .ToDictionary(static role => role.Id);
        var actorRoleIds = new HashSet<Guid>();
        var actorRoleKeys = new HashSet<string>(StringComparer.Ordinal);
        var policies = new List<DecisionPolicy>();

        foreach (var trustedRole in request.TrustedRoles)
        {
            var mappedRole = AuthorizationCatalog.MapTrustedRole(trustedRole);
            if (mappedRole is null
                || AuthorizationCatalog.FindSystemRole(mappedRole) is not { } role)
            {
                continue;
            }

            actorRoleIds.Add(role.Id);
            actorRoleKeys.Add(role.Key);
            AddRolePolicies(policies, request.ActorId, role, AuthorizationScope.Global);
        }

        foreach (var binding in snapshot.Bindings.Where(binding => string.Equals(
                     binding.ActorId,
                     request.ActorId,
                     StringComparison.Ordinal)))
        {
            if (!roles.TryGetValue(binding.RoleId, out var role)
                || role.Status != AuthorizationResourceStatus.Active)
            {
                continue;
            }

            AddRolePolicies(policies, request.ActorId, role, binding.Scope);
            if (Contains(binding.Scope, request.Scope))
            {
                actorRoleIds.Add(role.Id);
                actorRoleKeys.Add(role.Key);
            }
        }

        foreach (var rule in snapshot.PolicyRules)
        {
            var applies = rule.SubjectType switch
            {
                AuthorizationPolicySubjectType.Any => true,
                AuthorizationPolicySubjectType.Actor => string.Equals(
                    rule.Subject,
                    request.ActorId,
                    StringComparison.Ordinal),
                AuthorizationPolicySubjectType.Role =>
                    actorRoleKeys.Contains(rule.Subject)
                    || Guid.TryParse(rule.Subject, out var roleId)
                        && actorRoleIds.Contains(roleId),
                _ => false,
            };
            if (!applies)
            {
                continue;
            }

            policies.Add(new DecisionPolicy(
                rule.Id.ToString(),
                request.ActorId,
                rule.Scope,
                rule.Permission,
                rule.Effect));
        }

        var model = DefaultModel.CreateFromText(CasbinModel);
        var enforcer = new Enforcer(model);
        foreach (var policy in policies)
        {
            enforcer.AddPolicy(
                policy.Subject,
                ScopeValue(policy.Scope.TenantId),
                ScopeValue(policy.Scope.ApplicationId),
                ScopeValue(policy.Scope.EnvironmentId),
                policy.Permission,
                policy.Effect == AuthorizationPolicyEffect.Allow ? "allow" : "deny");
        }

        var tenant = RequestScopeValue(request.Scope.TenantId);
        var application = RequestScopeValue(request.Scope.ApplicationId);
        var environment = RequestScopeValue(request.Scope.EnvironmentId);
        var allowed = enforcer.Enforce(
            request.ActorId,
            tenant,
            application,
            environment,
            request.Permission);
        var matched = policies
            .Where(policy => Matches(policy, request, tenant, application, environment))
            .Select(static policy => policy.Id)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new AuthorizationDecisionResult(
            allowed,
            allowed
                ? "An active role or policy grants this permission."
                : matched.Length > 0
                    ? "An explicit deny policy overrides matching grants."
                    : "No active role or policy grants this permission.",
            matched,
            actorRoleKeys.Order(StringComparer.Ordinal).ToArray());
    }

    private static void AddRolePolicies(
        ICollection<DecisionPolicy> policies,
        string actorId,
        AuthorizationRole role,
        AuthorizationScope scope)
    {
        foreach (var permission in role.Permissions)
        {
            policies.Add(new DecisionPolicy(
                $"role:{role.Id}:{permission}",
                actorId,
                scope,
                permission,
                AuthorizationPolicyEffect.Allow));
        }
    }

    private static bool Matches(
        DecisionPolicy policy,
        AuthorizationDecisionRequest request,
        string tenant,
        string application,
        string environment) =>
        string.Equals(policy.Subject, request.ActorId, StringComparison.Ordinal)
        && MatchesScope(policy.Scope.TenantId, tenant)
        && MatchesScope(policy.Scope.ApplicationId, application)
        && MatchesScope(policy.Scope.EnvironmentId, environment)
        && (policy.Permission == "*"
            || string.Equals(policy.Permission, request.Permission, StringComparison.Ordinal));

    private static bool MatchesScope(Guid? policyValue, string requestValue) =>
        policyValue is null
        || string.Equals(
            policyValue.Value.ToString(),
            requestValue,
            StringComparison.OrdinalIgnoreCase);

    private static bool Contains(AuthorizationScope granted, AuthorizationScope requested) =>
        Contains(granted.TenantId, requested.TenantId)
        && Contains(granted.ApplicationId, requested.ApplicationId)
        && Contains(granted.EnvironmentId, requested.EnvironmentId);

    private static bool Contains(Guid? granted, Guid? requested) =>
        granted is null || granted == requested;

    private static string ScopeValue(Guid? value) => value?.ToString() ?? "*";

    private static string RequestScopeValue(Guid? value) => value?.ToString() ?? string.Empty;

    private sealed record DecisionPolicy(
        string Id,
        string Subject,
        AuthorizationScope Scope,
        string Permission,
        AuthorizationPolicyEffect Effect);
}
