using System.Data;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Authorization;

internal sealed class PostgreSqlAuthorizationStore(NpgsqlDataSource dataSource)
    : IAuthorizationStore
{
    public async Task<AuthorizationStorePage<AuthorizationRole>> ListRolesAsync(
        AuthorizationPageRequest page,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, key, display_name, description, permissions, is_system,
                   status, version, created_at, updated_at, archived_at
            FROM authorization.roles
            WHERE (@include_archived OR status = 1)
              AND (@query = ''
                   OR key ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset
            LIMIT @limit;
            """);
        AddPageParameters(command, page);
        var items = new List<AuthorizationRole>(page.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadRole(reader));
        }

        return TrimPage(items, page.PageSize);
    }

    public async Task<AuthorizationRole?> GetRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, key, display_name, description, permissions, is_system,
                   status, version, created_at, updated_at, archived_at
            FROM authorization.roles
            WHERE id = @id;
            """);
        command.Parameters.AddWithValue("id", roleId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRole(reader) : null;
    }

    public Task<bool> TryCreateRoleAsync(
        AuthorizationRole role,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(
            """
            INSERT INTO authorization.roles (
                id, key, display_name, description, permissions, is_system,
                status, version, created_at, updated_at, archived_at)
            VALUES (
                @id, @key, @display_name, @description, @permissions, @is_system,
                @status, @version, @created_at, @updated_at, @archived_at)
            ON CONFLICT DO NOTHING;
            """,
            command => AddRoleParameters(command, role),
            revision,
            cancellationToken);

    public Task<bool> TryUpdateRoleAsync(
        AuthorizationRole role,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(
            """
            UPDATE authorization.roles
            SET display_name = @display_name,
                description = @description,
                permissions = @permissions,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE id = @id AND version = @expected_version;
            """,
            command =>
            {
                AddRoleParameters(command, role);
                command.Parameters.AddWithValue("expected_version", expectedVersion);
            },
            revision,
            cancellationToken);

    public async Task<AuthorizationStorePage<AuthorizationRoleBinding>> ListRoleBindingsAsync(
        AuthorizationPageRequest page,
        string actorId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, actor_id, role_id, role_key, tenant_id, application_id, environment_id,
                   status, version, created_at, updated_at, archived_at
            FROM authorization.role_bindings
            WHERE (@include_archived OR status = 1)
              AND (@actor_id = '' OR actor_id ILIKE '%' || @actor_id || '%')
              AND (@tenant_id IS NULL OR tenant_id = @tenant_id)
            ORDER BY lower(actor_id), id
            OFFSET @offset
            LIMIT @limit;
            """);
        AddPageParameters(command, page);
        command.Parameters.AddWithValue("actor_id", actorId);
        AddNullableGuid(command, "tenant_id", tenantId);
        var items = new List<AuthorizationRoleBinding>(page.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadRoleBinding(reader));
        }

        return TrimPage(items, page.PageSize);
    }

    public async Task<AuthorizationRoleBinding?> GetRoleBindingAsync(
        Guid bindingId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, actor_id, role_id, role_key, tenant_id, application_id, environment_id,
                   status, version, created_at, updated_at, archived_at
            FROM authorization.role_bindings
            WHERE id = @id;
            """);
        command.Parameters.AddWithValue("id", bindingId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRoleBinding(reader) : null;
    }

    public Task<bool> TryCreateRoleBindingAsync(
        AuthorizationRoleBinding binding,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(
            """
            INSERT INTO authorization.role_bindings (
                id, actor_id, role_id, role_key, tenant_id, application_id, environment_id,
                status, version, created_at, updated_at, archived_at)
            VALUES (
                @id, @actor_id, @role_id, @role_key, @tenant_id, @application_id, @environment_id,
                @status, @version, @created_at, @updated_at, @archived_at)
            ON CONFLICT DO NOTHING;
            """,
            command => AddRoleBindingParameters(command, binding),
            revision,
            cancellationToken);

    public Task<bool> TryUpdateRoleBindingAsync(
        AuthorizationRoleBinding binding,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(
            """
            UPDATE authorization.role_bindings
            SET actor_id = @actor_id,
                role_id = @role_id,
                role_key = @role_key,
                tenant_id = @tenant_id,
                application_id = @application_id,
                environment_id = @environment_id,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE id = @id AND version = @expected_version;
            """,
            command =>
            {
                AddRoleBindingParameters(command, binding);
                command.Parameters.AddWithValue("expected_version", expectedVersion);
            },
            revision,
            cancellationToken);

    public async Task<AuthorizationStorePage<AuthorizationPolicyRule>> ListPolicyRulesAsync(
        AuthorizationPageRequest page,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, name, effect, subject_type, subject, tenant_id, application_id,
                   environment_id, permission, status, version, created_at, updated_at,
                   archived_at
            FROM authorization.policy_rules
            WHERE (@include_archived OR status = 1)
              AND (@tenant_id IS NULL OR tenant_id = @tenant_id)
              AND (@query = ''
                   OR name ILIKE '%' || @query || '%'
                   OR permission ILIKE '%' || @query || '%')
            ORDER BY lower(name), id
            OFFSET @offset
            LIMIT @limit;
            """);
        AddPageParameters(command, page);
        AddNullableGuid(command, "tenant_id", tenantId);
        var items = new List<AuthorizationPolicyRule>(page.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadPolicyRule(reader));
        }

        return TrimPage(items, page.PageSize);
    }

    public async Task<AuthorizationPolicyRule?> GetPolicyRuleAsync(
        Guid policyRuleId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, name, effect, subject_type, subject, tenant_id, application_id,
                   environment_id, permission, status, version, created_at, updated_at,
                   archived_at
            FROM authorization.policy_rules
            WHERE id = @id;
            """);
        command.Parameters.AddWithValue("id", policyRuleId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPolicyRule(reader) : null;
    }

    public Task<bool> TryCreatePolicyRuleAsync(
        AuthorizationPolicyRule policyRule,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(
            """
            INSERT INTO authorization.policy_rules (
                id, name, effect, subject_type, subject, tenant_id, application_id,
                environment_id, permission, status, version, created_at, updated_at,
                archived_at)
            VALUES (
                @id, @name, @effect, @subject_type, @subject, @tenant_id, @application_id,
                @environment_id, @permission, @status, @version, @created_at, @updated_at,
                @archived_at)
            ON CONFLICT DO NOTHING;
            """,
            command => AddPolicyRuleParameters(command, policyRule),
            revision,
            cancellationToken);

    public Task<bool> TryUpdatePolicyRuleAsync(
        AuthorizationPolicyRule policyRule,
        long expectedVersion,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(
            """
            UPDATE authorization.policy_rules
            SET name = @name,
                effect = @effect,
                subject_type = @subject_type,
                subject = @subject,
                tenant_id = @tenant_id,
                application_id = @application_id,
                environment_id = @environment_id,
                permission = @permission,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE id = @id AND version = @expected_version;
            """,
            command =>
            {
                AddPolicyRuleParameters(command, policyRule);
                command.Parameters.AddWithValue("expected_version", expectedVersion);
            },
            revision,
            cancellationToken);

    public async Task<AuthorizationStorePage<AuthorizationPolicyRevision>>
        ListPolicyRevisionsAsync(
            AuthorizationPageRequest page,
            string resourceType,
            string resourceId,
            CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, revision_number, change_type, resource_type, resource_id,
                   snapshot_hash, change_summary, created_by, created_at
            FROM authorization.policy_revisions
            WHERE (@resource_type = '' OR resource_type = @resource_type)
              AND (@resource_id = '' OR resource_id = @resource_id)
            ORDER BY revision_number DESC
            OFFSET @offset
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("resource_type", resourceType);
        command.Parameters.AddWithValue("resource_id", resourceId);
        command.Parameters.AddWithValue("offset", page.Offset);
        command.Parameters.AddWithValue("limit", page.PageSize + 1);
        var items = new List<AuthorizationPolicyRevision>(page.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadPolicyRevision(reader));
        }

        return TrimPage(items, page.PageSize);
    }

    public async Task<AuthorizationPolicySnapshot> GetPolicySnapshotAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var roles = new List<AuthorizationRole>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT id, key, display_name, description, permissions, is_system,
                       status, version, created_at, updated_at, archived_at
                FROM authorization.roles
                WHERE status = 1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                roles.Add(ReadRole(reader));
            }
        }

        var bindings = new List<AuthorizationRoleBinding>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT id, actor_id, role_id, role_key, tenant_id, application_id, environment_id,
                       status, version, created_at, updated_at, archived_at
                FROM authorization.role_bindings
                WHERE status = 1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                bindings.Add(ReadRoleBinding(reader));
            }
        }

        var policyRules = new List<AuthorizationPolicyRule>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT id, name, effect, subject_type, subject, tenant_id, application_id,
                       environment_id, permission, status, version, created_at, updated_at,
                       archived_at
                FROM authorization.policy_rules
                WHERE status = 1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                policyRules.Add(ReadPolicyRule(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new AuthorizationPolicySnapshot(roles, bindings, policyRules);
    }

    private async Task<bool> ExecuteWriteAsync(
        string sql,
        Action<NpgsqlCommand> addParameters,
        AuthorizationRevisionDraft revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                addParameters(command);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    return false;
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO authorization.policy_revisions (
                        id, change_type, resource_type, resource_id, snapshot_hash,
                        change_summary, created_by, created_at)
                    VALUES (
                        @id, @change_type, @resource_type, @resource_id, @snapshot_hash,
                        @change_summary, @created_by, @created_at);
                    """;
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("change_type", revision.ChangeType);
                command.Parameters.AddWithValue("resource_type", revision.ResourceType);
                command.Parameters.AddWithValue("resource_id", revision.ResourceId);
                command.Parameters.AddWithValue("snapshot_hash", revision.SnapshotHash);
                command.Parameters.AddWithValue("change_summary", revision.ChangeSummary);
                command.Parameters.AddWithValue("created_by", revision.CreatedBy);
                command.Parameters.AddWithValue("created_at", revision.CreatedAt.UtcDateTime);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    private static void AddPageParameters(
        NpgsqlCommand command,
        AuthorizationPageRequest page)
    {
        command.Parameters.AddWithValue("include_archived", page.IncludeArchived);
        command.Parameters.AddWithValue("query", page.Query);
        command.Parameters.AddWithValue("offset", page.Offset);
        command.Parameters.AddWithValue("limit", page.PageSize + 1);
    }

    private static void AddRoleParameters(NpgsqlCommand command, AuthorizationRole role)
    {
        command.Parameters.AddWithValue("id", role.Id);
        command.Parameters.AddWithValue("key", role.Key);
        command.Parameters.AddWithValue("display_name", role.DisplayName);
        command.Parameters.AddWithValue("description", role.Description);
        command.Parameters.Add(
            new NpgsqlParameter<string[]>("permissions", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                TypedValue = role.Permissions.ToArray(),
            });
        command.Parameters.AddWithValue("is_system", role.IsSystem);
        command.Parameters.AddWithValue("status", (short)role.Status);
        command.Parameters.AddWithValue("version", role.Version);
        command.Parameters.AddWithValue("created_at", role.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", role.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "archived_at", role.ArchivedAt);
    }

    private static void AddRoleBindingParameters(
        NpgsqlCommand command,
        AuthorizationRoleBinding binding)
    {
        command.Parameters.AddWithValue("id", binding.Id);
        command.Parameters.AddWithValue("actor_id", binding.ActorId);
        command.Parameters.AddWithValue("role_id", binding.RoleId);
        command.Parameters.AddWithValue("role_key", binding.RoleKey);
        AddScopeParameters(command, binding.Scope);
        command.Parameters.AddWithValue("status", (short)binding.Status);
        command.Parameters.AddWithValue("version", binding.Version);
        command.Parameters.AddWithValue("created_at", binding.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", binding.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "archived_at", binding.ArchivedAt);
    }

    private static void AddPolicyRuleParameters(
        NpgsqlCommand command,
        AuthorizationPolicyRule policyRule)
    {
        command.Parameters.AddWithValue("id", policyRule.Id);
        command.Parameters.AddWithValue("name", policyRule.Name);
        command.Parameters.AddWithValue("effect", (short)policyRule.Effect);
        command.Parameters.AddWithValue("subject_type", (short)policyRule.SubjectType);
        command.Parameters.AddWithValue("subject", policyRule.Subject);
        AddScopeParameters(command, policyRule.Scope);
        command.Parameters.AddWithValue("permission", policyRule.Permission);
        command.Parameters.AddWithValue("status", (short)policyRule.Status);
        command.Parameters.AddWithValue("version", policyRule.Version);
        command.Parameters.AddWithValue("created_at", policyRule.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", policyRule.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "archived_at", policyRule.ArchivedAt);
    }

    private static void AddScopeParameters(NpgsqlCommand command, AuthorizationScope scope)
    {
        AddNullableGuid(command, "tenant_id", scope.TenantId);
        AddNullableGuid(command, "application_id", scope.ApplicationId);
        AddNullableGuid(command, "environment_id", scope.EnvironmentId);
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.Uuid)
            {
                Value = value is null ? DBNull.Value : value.Value,
            });

    private static void AddNullableTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset? value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
            {
                Value = value is null ? DBNull.Value : value.Value.UtcDateTime,
            });

    private static AuthorizationRole ReadRole(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetFieldValue<string[]>(4),
        reader.GetBoolean(5),
        (AuthorizationResourceStatus)reader.GetInt16(6),
        reader.GetInt64(7),
        ToDateTimeOffset(reader.GetDateTime(8)),
        ToDateTimeOffset(reader.GetDateTime(9)),
        reader.IsDBNull(10) ? null : ToDateTimeOffset(reader.GetDateTime(10)));

    private static AuthorizationRoleBinding ReadRoleBinding(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetGuid(2),
        reader.GetString(3),
        ReadScope(reader, 4),
        (AuthorizationResourceStatus)reader.GetInt16(7),
        reader.GetInt64(8),
        ToDateTimeOffset(reader.GetDateTime(9)),
        ToDateTimeOffset(reader.GetDateTime(10)),
        reader.IsDBNull(11) ? null : ToDateTimeOffset(reader.GetDateTime(11)));

    private static AuthorizationPolicyRule ReadPolicyRule(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        (AuthorizationPolicyEffect)reader.GetInt16(2),
        (AuthorizationPolicySubjectType)reader.GetInt16(3),
        reader.GetString(4),
        ReadScope(reader, 5),
        reader.GetString(8),
        (AuthorizationResourceStatus)reader.GetInt16(9),
        reader.GetInt64(10),
        ToDateTimeOffset(reader.GetDateTime(11)),
        ToDateTimeOffset(reader.GetDateTime(12)),
        reader.IsDBNull(13) ? null : ToDateTimeOffset(reader.GetDateTime(13)));

    private static AuthorizationPolicyRevision ReadPolicyRevision(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetInt64(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        ToDateTimeOffset(reader.GetDateTime(8)));

    private static AuthorizationScope ReadScope(NpgsqlDataReader reader, int ordinal) => new(
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal),
        reader.IsDBNull(ordinal + 1) ? null : reader.GetGuid(ordinal + 1),
        reader.IsDBNull(ordinal + 2) ? null : reader.GetGuid(ordinal + 2));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static AuthorizationStorePage<T> TrimPage<T>(List<T> items, int pageSize)
    {
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new AuthorizationStorePage<T>(items, hasMore);
    }
}
