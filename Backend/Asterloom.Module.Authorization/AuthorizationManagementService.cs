using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Requests;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Authorization;

public sealed partial class AuthorizationManagementService(
    IAuthorizationStore store,
    AuthorizationDecisionService decisionService,
    IAsterloomRequestContextAccessor requestContextAccessor,
    TimeProvider timeProvider)
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    public static AuthorizationListResult<PermissionDefinition> ListPermissions(
        int pageSize,
        string? pageToken,
        string? query)
    {
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived: true);
        var filtered = AuthorizationCatalog.Permissions
            .Where(permission => string.IsNullOrEmpty(page.Query)
                || permission.Key.Contains(page.Query, StringComparison.OrdinalIgnoreCase)
                || permission.DisplayName.Contains(
                    page.Query,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(static permission => permission.Key, StringComparer.Ordinal)
            .ToArray();
        return Page(filtered, page);
    }

    public async Task<AuthorizationListResult<AuthorizationRole>> ListRolesAsync(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var systemRoles = AuthorizationCatalog.SystemRoles
            .Where(role => string.IsNullOrEmpty(page.Query)
                || role.Key.Contains(page.Query, StringComparison.OrdinalIgnoreCase)
                || role.DisplayName.Contains(page.Query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static role => role.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var items = new List<AuthorizationRole>(page.PageSize);
        var customOffset = Math.Max(0, page.Offset - systemRoles.Length);
        if (page.Offset < systemRoles.Length)
        {
            items.AddRange(systemRoles.Skip(page.Offset).Take(page.PageSize));
        }

        var remaining = page.PageSize - items.Count;
        var customPage = await store.ListRolesAsync(
            page with { Offset = customOffset, PageSize = Math.Max(1, remaining) },
            cancellationToken);
        if (remaining > 0)
        {
            items.AddRange(customPage.Items.Take(remaining));
        }

        var hasMore = page.Offset + items.Count < systemRoles.Length
            || customPage.HasMore
            || remaining == 0 && customPage.Items.Count > 0;
        return ToListResult(items, page.Offset, hasMore);
    }

    public async Task<AuthorizationRole> CreateRoleAsync(
        string key,
        string displayName,
        string description,
        IEnumerable<string> permissions,
        CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeKey(key);
        if (AuthorizationCatalog.FindSystemRole(normalizedKey) is not null)
        {
            throw AlreadyExists("role_key_exists", "A role with this key already exists.");
        }

        var now = timeProvider.GetUtcNow();
        var role = new AuthorizationRole(
            Guid.CreateVersion7(),
            normalizedKey,
            NormalizeName(displayName),
            NormalizeDescription(description),
            NormalizePermissions(permissions),
            IsSystem: false,
            AuthorizationResourceStatus.Active,
            Version: 1,
            now,
            now,
            ArchivedAt: null);
        var revision = CreateRevision("create", "role", role.Id.ToString(), role);
        if (!await store.TryCreateRoleAsync(role, revision, cancellationToken))
        {
            throw AlreadyExists("role_key_exists", "A role with this key already exists.");
        }

        return role;
    }

    public async Task<AuthorizationRole> UpdateRoleAsync(
        string roleId,
        string displayName,
        string description,
        IEnumerable<string> permissions,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var current = await RequireCustomRoleAsync(roleId, cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current.Status, "role");
        var updated = current with
        {
            DisplayName = NormalizeName(displayName),
            Description = NormalizeDescription(description),
            Permissions = NormalizePermissions(permissions),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        return await UpdateRoleAsync(current, updated, "update", cancellationToken);
    }

    public async Task<AuthorizationRole> ArchiveRoleAsync(
        string roleId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var current = await RequireCustomRoleAsync(roleId, cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == AuthorizationResourceStatus.Archived)
        {
            return current;
        }

        var now = timeProvider.GetUtcNow();
        return await UpdateRoleAsync(
            current,
            current with
            {
                Status = AuthorizationResourceStatus.Archived,
                Version = current.Version + 1,
                UpdatedAt = now,
                ArchivedAt = now,
            },
            "archive",
            cancellationToken);
    }

    public async Task<AuthorizationRole> RestoreRoleAsync(
        string roleId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var current = await RequireCustomRoleAsync(roleId, cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == AuthorizationResourceStatus.Active)
        {
            return current;
        }

        return await UpdateRoleAsync(
            current,
            current with
            {
                Status = AuthorizationResourceStatus.Active,
                Version = current.Version + 1,
                UpdatedAt = timeProvider.GetUtcNow(),
                ArchivedAt = null,
            },
            "restore",
            cancellationToken);
    }

    public async Task<AuthorizationListResult<AuthorizationRoleBinding>>
        ListRoleBindingsAsync(
            int pageSize,
            string? pageToken,
            string? actorId,
            string? tenantId,
            bool includeArchived,
            CancellationToken cancellationToken)
    {
        var page = CreatePageRequest(pageSize, pageToken, query: null, includeArchived);
        var result = await store.ListRoleBindingsAsync(
            page,
            NormalizeOptionalActor(actorId),
            ParseOptionalId(tenantId, "tenantId"),
            cancellationToken);
        return ToListResult(result.Items, page.Offset, result.HasMore);
    }

    public async Task<AuthorizationRoleBinding> SetRoleBindingAsync(
        string bindingId,
        string actorId,
        string roleId,
        AuthorizationScope scope,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var parsedBindingId = ParseId(bindingId, "bindingId");
        var normalizedActor = NormalizeActor(actorId);
        var parsedRoleId = ParseId(roleId, "roleId");
        var role = await FindRoleAsync(parsedRoleId, cancellationToken)
            ?? throw NotFound("role_not_found", "The role was not found.");
        RequireActive(role.Status, "role");
        ValidateScope(scope);
        var current = await store.GetRoleBindingAsync(parsedBindingId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (current is null)
        {
            if (expectedVersion != 0)
            {
                throw NotFound("role_binding_not_found", "The role binding was not found.");
            }

            var created = new AuthorizationRoleBinding(
                parsedBindingId,
                normalizedActor,
                parsedRoleId,
                role.Key,
                scope,
                AuthorizationResourceStatus.Active,
                Version: 1,
                now,
                now,
                ArchivedAt: null);
            var revision = CreateRevision(
                "create",
                "role_binding",
                created.Id.ToString(),
                created);
            if (!await store.TryCreateRoleBindingAsync(created, revision, cancellationToken))
            {
                throw Conflict("role_binding_conflict", "An equivalent role binding exists.");
            }

            return created;
        }

        RequireVersion(current.Version, expectedVersion);
        var updated = current with
        {
            ActorId = normalizedActor,
            RoleId = parsedRoleId,
            RoleKey = role.Key,
            Scope = scope,
            Status = AuthorizationResourceStatus.Active,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = null,
        };
        var updateRevision = CreateRevision(
            current.Status == AuthorizationResourceStatus.Archived ? "restore" : "update",
            "role_binding",
            updated.Id.ToString(),
            updated);
        if (!await store.TryUpdateRoleBindingAsync(
                updated,
                current.Version,
                updateRevision,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public async Task<AuthorizationRoleBinding> RemoveRoleBindingAsync(
        string bindingId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var current = await store.GetRoleBindingAsync(
            ParseId(bindingId, "bindingId"),
            cancellationToken)
            ?? throw NotFound("role_binding_not_found", "The role binding was not found.");
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == AuthorizationResourceStatus.Archived)
        {
            return current;
        }

        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = AuthorizationResourceStatus.Archived,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = now,
        };
        var revision = CreateRevision(
            "remove",
            "role_binding",
            updated.Id.ToString(),
            updated);
        if (!await store.TryUpdateRoleBindingAsync(
                updated,
                current.Version,
                revision,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public async Task<AuthorizationListResult<AuthorizationPolicyRule>>
        ListPolicyRulesAsync(
            int pageSize,
            string? pageToken,
            string? query,
            string? tenantId,
            bool includeArchived,
            CancellationToken cancellationToken)
    {
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var result = await store.ListPolicyRulesAsync(
            page,
            ParseOptionalId(tenantId, "tenantId"),
            cancellationToken);
        return ToListResult(result.Items, page.Offset, result.HasMore);
    }

    public async Task<AuthorizationPolicyRule> CreatePolicyRuleAsync(
        string name,
        AuthorizationPolicyEffect effect,
        AuthorizationPolicySubjectType subjectType,
        string subject,
        AuthorizationScope scope,
        string permission,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var policyRule = new AuthorizationPolicyRule(
            Guid.CreateVersion7(),
            NormalizeName(name),
            ValidateEffect(effect),
            ValidateSubjectType(subjectType),
            NormalizeSubject(subjectType, subject),
            ValidateScope(scope),
            NormalizePermission(permission),
            AuthorizationResourceStatus.Active,
            Version: 1,
            now,
            now,
            ArchivedAt: null);
        var revision = CreateRevision(
            "create",
            "policy_rule",
            policyRule.Id.ToString(),
            policyRule);
        if (!await store.TryCreatePolicyRuleAsync(policyRule, revision, cancellationToken))
        {
            throw Conflict("policy_rule_conflict", "The policy rule could not be created.");
        }

        return policyRule;
    }

    public async Task<AuthorizationPolicyRule> UpdatePolicyRuleAsync(
        string policyRuleId,
        string name,
        AuthorizationPolicyEffect effect,
        AuthorizationPolicySubjectType subjectType,
        string subject,
        AuthorizationScope scope,
        string permission,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var current = await RequirePolicyRuleAsync(policyRuleId, cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current.Status, "policy rule");
        var updated = current with
        {
            Name = NormalizeName(name),
            Effect = ValidateEffect(effect),
            SubjectType = ValidateSubjectType(subjectType),
            Subject = NormalizeSubject(subjectType, subject),
            Scope = ValidateScope(scope),
            Permission = NormalizePermission(permission),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        return await UpdatePolicyRuleAsync(current, updated, "update", cancellationToken);
    }

    public async Task<AuthorizationPolicyRule> ArchivePolicyRuleAsync(
        string policyRuleId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var current = await RequirePolicyRuleAsync(policyRuleId, cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == AuthorizationResourceStatus.Archived)
        {
            return current;
        }

        var now = timeProvider.GetUtcNow();
        return await UpdatePolicyRuleAsync(
            current,
            current with
            {
                Status = AuthorizationResourceStatus.Archived,
                Version = current.Version + 1,
                UpdatedAt = now,
                ArchivedAt = now,
            },
            "archive",
            cancellationToken);
    }

    public async Task<AuthorizationPolicyRule> RestorePolicyRuleAsync(
        string policyRuleId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var current = await RequirePolicyRuleAsync(policyRuleId, cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == AuthorizationResourceStatus.Active)
        {
            return current;
        }

        return await UpdatePolicyRuleAsync(
            current,
            current with
            {
                Status = AuthorizationResourceStatus.Active,
                Version = current.Version + 1,
                UpdatedAt = timeProvider.GetUtcNow(),
                ArchivedAt = null,
            },
            "restore",
            cancellationToken);
    }

    public async Task<AuthorizationListResult<AuthorizationPolicyRevision>>
        ListPolicyRevisionsAsync(
            int pageSize,
            string? pageToken,
            string? resourceType,
            string? resourceId,
            CancellationToken cancellationToken)
    {
        var page = CreatePageRequest(pageSize, pageToken, query: null, includeArchived: true);
        var result = await store.ListPolicyRevisionsAsync(
            page,
            NormalizeOptional(resourceType, 64),
            NormalizeOptional(resourceId, 200),
            cancellationToken);
        return ToListResult(result.Items, page.Offset, result.HasMore);
    }

    public Task<AuthorizationDecisionResult> SimulateAsync(
        AuthorizationDecisionRequest request,
        CancellationToken cancellationToken)
    {
        ValidateDecisionRequest(request);
        return decisionService.DecideAsync(request, cancellationToken);
    }

    private async Task<AuthorizationRole> UpdateRoleAsync(
        AuthorizationRole current,
        AuthorizationRole updated,
        string changeType,
        CancellationToken cancellationToken)
    {
        var revision = CreateRevision(changeType, "role", updated.Id.ToString(), updated);
        if (!await store.TryUpdateRoleAsync(
                updated,
                current.Version,
                revision,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    private async Task<AuthorizationPolicyRule> UpdatePolicyRuleAsync(
        AuthorizationPolicyRule current,
        AuthorizationPolicyRule updated,
        string changeType,
        CancellationToken cancellationToken)
    {
        var revision = CreateRevision(
            changeType,
            "policy_rule",
            updated.Id.ToString(),
            updated);
        if (!await store.TryUpdatePolicyRuleAsync(
                updated,
                current.Version,
                revision,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    private async Task<AuthorizationRole> RequireCustomRoleAsync(
        string roleId,
        CancellationToken cancellationToken)
    {
        var id = ParseId(roleId, "roleId");
        if (AuthorizationCatalog.FindSystemRole(id) is not null)
        {
            throw FailedPrecondition(
                "system_role_immutable",
                "System roles are defined by the platform and cannot be changed.");
        }

        return await store.GetRoleAsync(id, cancellationToken)
            ?? throw NotFound("role_not_found", "The role was not found.");
    }

    private Task<AuthorizationRole?> FindRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken) =>
        AuthorizationCatalog.FindSystemRole(roleId) is { } systemRole
            ? Task.FromResult<AuthorizationRole?>(systemRole)
            : store.GetRoleAsync(roleId, cancellationToken);

    private async Task<AuthorizationPolicyRule> RequirePolicyRuleAsync(
        string policyRuleId,
        CancellationToken cancellationToken) =>
        await store.GetPolicyRuleAsync(
            ParseId(policyRuleId, "policyRuleId"),
            cancellationToken)
        ?? throw NotFound("policy_rule_not_found", "The policy rule was not found.");

    private AuthorizationRevisionDraft CreateRevision(
        string changeType,
        string resourceType,
        string resourceId,
        object snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new AuthorizationRevisionDraft(
            changeType,
            resourceType,
            resourceId,
            hash,
            $"{changeType} {resourceType} {resourceId}",
            requestContextAccessor.Current?.ActorId ?? "system",
            timeProvider.GetUtcNow());
    }

    private static AuthorizationScope ValidateScope(AuthorizationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.EnvironmentId is not null && scope.ApplicationId is null)
        {
            throw Invalid("scope.applicationId", "Application is required for environment scope.");
        }

        if (scope.ApplicationId is not null && scope.TenantId is null)
        {
            throw Invalid("scope.tenantId", "Tenant is required for application scope.");
        }

        return scope;
    }

    private static void ValidateDecisionRequest(AuthorizationDecisionRequest request)
    {
        NormalizeActor(request.ActorId);
        ValidateScope(request.Scope);
        NormalizePermission(request.Permission);
        if (request.TrustedRoles.Count > 20)
        {
            throw Invalid("trustedRoles", "At most 20 trusted roles are accepted.");
        }
    }

    private static AuthorizationPolicyEffect ValidateEffect(AuthorizationPolicyEffect effect) =>
        effect is AuthorizationPolicyEffect.Allow or AuthorizationPolicyEffect.Deny
            ? effect
            : throw Invalid("effect", "Policy effect must be allow or deny.");

    private static AuthorizationPolicySubjectType ValidateSubjectType(
        AuthorizationPolicySubjectType subjectType) =>
        subjectType is AuthorizationPolicySubjectType.Actor
            or AuthorizationPolicySubjectType.Role
            or AuthorizationPolicySubjectType.Any
            ? subjectType
            : throw Invalid("subjectType", "Policy subject type is invalid.");

    private static string NormalizeSubject(
        AuthorizationPolicySubjectType subjectType,
        string? subject) =>
        subjectType == AuthorizationPolicySubjectType.Any
            ? "*"
            : NormalizeActor(subject);

    private static string[] NormalizePermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var normalized = permissions
            .Select(NormalizePermission)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw Invalid("permissions", "At least one permission is required.");
        }

        if (normalized.Length > 200)
        {
            throw Invalid("permissions", "At most 200 permissions are allowed.");
        }

        return normalized;
    }

    private static string NormalizePermission(string? permission)
    {
        var normalized = NormalizeOptional(permission, 200);
        if (string.IsNullOrEmpty(normalized) || !AuthorizationCatalog.IsKnownPermission(normalized))
        {
            throw Invalid("permission", "Permission is not present in the catalog.");
        }

        return normalized;
    }

    private static string NormalizeKey(string? key)
    {
        var normalized = NormalizeOptional(key, 64).ToLowerInvariant();
        if (!KeyPattern().IsMatch(normalized))
        {
            throw Invalid(
                "key",
                "Use 3-64 lowercase letters, numbers, or hyphens; start and end with a letter or number.");
        }

        return normalized;
    }

    private static string NormalizeName(string? value)
    {
        var normalized = NormalizeOptional(value, 200);
        if (string.IsNullOrEmpty(normalized))
        {
            throw Invalid("displayName", "A display name is required.");
        }

        return normalized;
    }

    private static string NormalizeDescription(string? value) => NormalizeOptional(value, 1000);

    private static string NormalizeActor(string? value)
    {
        var normalized = NormalizeOptional(value, 200);
        if (string.IsNullOrEmpty(normalized))
        {
            throw Invalid("actorId", "An actor ID is required.");
        }

        return normalized;
    }

    private static string NormalizeOptionalActor(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeActor(value);

    private static string NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxLength)
        {
            throw Invalid("value", $"Value must not exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static Guid ParseId(string value, string field)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw Invalid(field, "A valid identifier is required.");
        }

        return id;
    }

    private static Guid? ParseOptionalId(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseId(value, field);

    private static void RequireVersion(long currentVersion, long expectedVersion)
    {
        if (expectedVersion <= 0)
        {
            throw Invalid("expectedVersion", "Expected version must be positive.");
        }

        if (currentVersion != expectedVersion)
        {
            throw VersionConflict();
        }
    }

    private static void RequireActive(AuthorizationResourceStatus status, string resource)
    {
        if (status != AuthorizationResourceStatus.Active)
        {
            throw FailedPrecondition(
                $"{resource.Replace(' ', '_')}_archived",
                $"The {resource} is archived and must be restored first.");
        }
    }

    private static AuthorizationPageRequest CreatePageRequest(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived)
    {
        var normalizedSize = pageSize == 0 ? DefaultPageSize : pageSize;
        if (normalizedSize is < 1 or > MaximumPageSize)
        {
            throw Invalid("pageSize", $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var offset = 0;
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(pageToken));
                if (!int.TryParse(decoded, out offset) || offset < 0)
                {
                    throw new FormatException();
                }
            }
            catch (FormatException)
            {
                throw Invalid("pageToken", "Page token is invalid.");
            }
        }

        return new AuthorizationPageRequest(
            offset,
            normalizedSize,
            NormalizeOptional(query, 200),
            includeArchived);
    }

    private static AuthorizationListResult<T> Page<T>(
        T[] source,
        AuthorizationPageRequest page)
    {
        var items = source.Skip(page.Offset).Take(page.PageSize).ToArray();
        return ToListResult(
            items,
            page.Offset,
            page.Offset + items.Length < source.Length);
    }

    private static AuthorizationListResult<T> ToListResult<T>(
        IReadOnlyList<T> items,
        int offset,
        bool hasMore) =>
        new(
            items,
            hasMore
                ? WebEncoders.Base64UrlEncode(
                    Encoding.UTF8.GetBytes((offset + items.Count).ToString(
                        CultureInfo.InvariantCulture)))
                : string.Empty);

    private static AsterloomException Invalid(string field, string message) =>
        new(
            AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [field] = [message],
            });

    private static AsterloomException NotFound(string code, string message) =>
        new(AsterloomErrorKind.NotFound, code, message);

    private static AsterloomException AlreadyExists(string code, string message) =>
        new(AsterloomErrorKind.AlreadyExists, code, message);

    private static AsterloomException Conflict(string code, string message) =>
        new(AsterloomErrorKind.Conflict, code, message);

    private static AsterloomException FailedPrecondition(string code, string message) =>
        new(AsterloomErrorKind.FailedPrecondition, code, message);

    private static AsterloomException VersionConflict() =>
        Conflict(
            "version_conflict",
            "The resource changed since it was loaded. Reload and try again.");

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

public sealed record AuthorizationListResult<T>(
    IReadOnlyList<T> Items,
    string NextPageToken);
