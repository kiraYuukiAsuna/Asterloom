using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Platform;

internal sealed class PostgreSqlPlatformResourceStore(NpgsqlDataSource dataSource)
    : IPlatformResourceStore
{
    public async Task<PlatformPage<PlatformTenant>> ListTenantsAsync(
        PlatformPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, slug, display_name, status, version, created_at, updated_at, archived_at
            FROM platform.tenants
            WHERE (@include_inactive OR status = 1)
              AND (@query IS NULL
                   OR slug ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset
            LIMIT @limit;
            """);
        AddPageParameters(command, request);
        var items = new List<PlatformTenant>(request.Limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadTenant(reader));
        }

        return TrimPage(items, request.Limit);
    }

    public async Task<PlatformTenant?> GetTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, slug, display_name, status, version, created_at, updated_at, archived_at
            FROM platform.tenants
            WHERE id = @id;
            """);
        command.Parameters.AddWithValue("id", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTenant(reader) : null;
    }

    public async Task<bool> TryCreateTenantAsync(
        PlatformTenant tenant,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO platform.tenants (
                id, slug, display_name, status, version, created_at, updated_at, archived_at)
            VALUES (
                @id, @slug, @display_name, @status, @version, @created_at, @updated_at, @archived_at)
            ON CONFLICT (slug) DO NOTHING;
            """);
        AddTenantParameters(command, tenant);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateTenantAsync(
        PlatformTenant tenant,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE platform.tenants
            SET display_name = @display_name,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE id = @id AND version = @expected_version;
            """);
        AddTenantParameters(command, tenant);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<PlatformPage<PlatformApplication>> ListApplicationsAsync(
        Guid tenantId,
        PlatformPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, tenant_id, slug, display_name, status, version,
                   created_at, updated_at, archived_at
            FROM platform.applications
            WHERE tenant_id = @tenant_id
              AND (@include_inactive OR status = 1)
              AND (@query IS NULL
                   OR slug ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddPageParameters(command, request);
        var items = new List<PlatformApplication>(request.Limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadApplication(reader));
        }

        return TrimPage(items, request.Limit);
    }

    public async Task<PlatformApplication?> GetApplicationAsync(
        Guid tenantId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, tenant_id, slug, display_name, status, version,
                   created_at, updated_at, archived_at
            FROM platform.applications
            WHERE tenant_id = @tenant_id AND id = @id;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", applicationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadApplication(reader) : null;
    }

    public async Task<bool> TryCreateApplicationAsync(
        PlatformApplication application,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO platform.applications (
                id, tenant_id, slug, display_name, status, version,
                created_at, updated_at, archived_at)
            VALUES (
                @id, @tenant_id, @slug, @display_name, @status, @version,
                @created_at, @updated_at, @archived_at)
            ON CONFLICT (tenant_id, slug) DO NOTHING;
            """);
        AddApplicationParameters(command, application);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateApplicationAsync(
        PlatformApplication application,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE platform.applications
            SET display_name = @display_name,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE tenant_id = @tenant_id AND id = @id AND version = @expected_version;
            """);
        AddApplicationParameters(command, application);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<PlatformPage<PlatformEnvironment>> ListEnvironmentsAsync(
        Guid tenantId,
        Guid applicationId,
        PlatformPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, tenant_id, application_id, slug, display_name, environment_type,
                   is_protected, status, version, created_at, updated_at, archived_at
            FROM platform.environments
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND (@include_inactive OR status = 1)
              AND (@query IS NULL
                   OR slug ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("application_id", applicationId);
        AddPageParameters(command, request);
        var items = new List<PlatformEnvironment>(request.Limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadEnvironment(reader));
        }

        return TrimPage(items, request.Limit);
    }

    public async Task<PlatformEnvironment?> GetEnvironmentAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, tenant_id, application_id, slug, display_name, environment_type,
                   is_protected, status, version, created_at, updated_at, archived_at
            FROM platform.environments
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND id = @id;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("application_id", applicationId);
        command.Parameters.AddWithValue("id", environmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEnvironment(reader) : null;
    }

    public async Task<bool> TryCreateEnvironmentAsync(
        PlatformEnvironment environment,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO platform.environments (
                id, tenant_id, application_id, slug, display_name, environment_type,
                is_protected, status, version, created_at, updated_at, archived_at)
            VALUES (
                @id, @tenant_id, @application_id, @slug, @display_name, @environment_type,
                @is_protected, @status, @version, @created_at, @updated_at, @archived_at)
            ON CONFLICT (application_id, slug) DO NOTHING;
            """);
        AddEnvironmentParameters(command, environment);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateEnvironmentAsync(
        PlatformEnvironment environment,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE platform.environments
            SET display_name = @display_name,
                environment_type = @environment_type,
                is_protected = @is_protected,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND id = @id
              AND version = @expected_version;
            """);
        AddEnvironmentParameters(command, environment);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<PlatformPage<PlatformTenantMembership>> ListTenantMembershipsAsync(
        Guid tenantId,
        PlatformPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT tenant_id, actor_id, status, version, created_at, updated_at
            FROM platform.tenant_memberships
            WHERE tenant_id = @tenant_id
              AND (@include_inactive OR status = 1)
            ORDER BY actor_id
            OFFSET @offset
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddPageParameters(command, request);
        var items = new List<PlatformTenantMembership>(request.Limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadMembership(reader));
        }

        return TrimPage(items, request.Limit);
    }

    public async Task<PlatformTenantMembership?> GetTenantMembershipAsync(
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT tenant_id, actor_id, status, version, created_at, updated_at
            FROM platform.tenant_memberships
            WHERE tenant_id = @tenant_id AND actor_id = @actor_id;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("actor_id", actorId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMembership(reader) : null;
    }

    public async Task<bool> TryCreateTenantMembershipAsync(
        PlatformTenantMembership membership,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO platform.tenant_memberships (
                tenant_id, actor_id, status, version, created_at, updated_at)
            VALUES (
                @tenant_id, @actor_id, @status, @version, @created_at, @updated_at)
            ON CONFLICT (tenant_id, actor_id) DO NOTHING;
            """);
        AddMembershipParameters(command, membership);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateTenantMembershipAsync(
        PlatformTenantMembership membership,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE platform.tenant_memberships
            SET status = @status,
                version = @version,
                updated_at = @updated_at
            WHERE tenant_id = @tenant_id
              AND actor_id = @actor_id
              AND version = @expected_version;
            """);
        AddMembershipParameters(command, membership);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void AddPageParameters(NpgsqlCommand command, PlatformPageRequest request)
    {
        command.Parameters.AddWithValue("include_inactive", request.IncludeInactive);
        command.Parameters.Add(
            new NpgsqlParameter("query", NpgsqlDbType.Text)
            {
                Value = request.Query is null ? DBNull.Value : request.Query,
            });
        command.Parameters.AddWithValue("offset", request.Offset);
        command.Parameters.AddWithValue("limit", request.Limit + 1);
    }

    private static void AddTenantParameters(NpgsqlCommand command, PlatformTenant tenant)
    {
        command.Parameters.AddWithValue("id", tenant.Id);
        command.Parameters.AddWithValue("slug", tenant.Slug);
        command.Parameters.AddWithValue("display_name", tenant.DisplayName);
        command.Parameters.AddWithValue("status", (short)tenant.Status);
        command.Parameters.AddWithValue("version", tenant.Version);
        command.Parameters.AddWithValue("created_at", tenant.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", tenant.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "archived_at", tenant.ArchivedAt);
    }

    private static void AddApplicationParameters(
        NpgsqlCommand command,
        PlatformApplication application)
    {
        command.Parameters.AddWithValue("id", application.Id);
        command.Parameters.AddWithValue("tenant_id", application.TenantId);
        command.Parameters.AddWithValue("slug", application.Slug);
        command.Parameters.AddWithValue("display_name", application.DisplayName);
        command.Parameters.AddWithValue("status", (short)application.Status);
        command.Parameters.AddWithValue("version", application.Version);
        command.Parameters.AddWithValue("created_at", application.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", application.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "archived_at", application.ArchivedAt);
    }

    private static void AddEnvironmentParameters(
        NpgsqlCommand command,
        PlatformEnvironment environment)
    {
        command.Parameters.AddWithValue("id", environment.Id);
        command.Parameters.AddWithValue("tenant_id", environment.TenantId);
        command.Parameters.AddWithValue("application_id", environment.ApplicationId);
        command.Parameters.AddWithValue("slug", environment.Slug);
        command.Parameters.AddWithValue("display_name", environment.DisplayName);
        command.Parameters.AddWithValue("environment_type", (short)environment.EnvironmentType);
        command.Parameters.AddWithValue("is_protected", environment.IsProtected);
        command.Parameters.AddWithValue("status", (short)environment.Status);
        command.Parameters.AddWithValue("version", environment.Version);
        command.Parameters.AddWithValue("created_at", environment.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", environment.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "archived_at", environment.ArchivedAt);
    }

    private static void AddMembershipParameters(
        NpgsqlCommand command,
        PlatformTenantMembership membership)
    {
        command.Parameters.AddWithValue("tenant_id", membership.TenantId);
        command.Parameters.AddWithValue("actor_id", membership.ActorId);
        command.Parameters.AddWithValue("status", (short)membership.Status);
        command.Parameters.AddWithValue("version", membership.Version);
        command.Parameters.AddWithValue("created_at", membership.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", membership.UpdatedAt.UtcDateTime);
    }

    private static void AddNullableTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset? value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
            {
                Value = value is null ? DBNull.Value : value.Value.UtcDateTime,
            });

    private static PlatformTenant ReadTenant(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        (PlatformResourceStatus)reader.GetInt16(3),
        reader.GetInt64(4),
        ToDateTimeOffset(reader.GetDateTime(5)),
        ToDateTimeOffset(reader.GetDateTime(6)),
        reader.IsDBNull(7) ? null : ToDateTimeOffset(reader.GetDateTime(7)));

    private static PlatformApplication ReadApplication(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        (PlatformResourceStatus)reader.GetInt16(4),
        reader.GetInt64(5),
        ToDateTimeOffset(reader.GetDateTime(6)),
        ToDateTimeOffset(reader.GetDateTime(7)),
        reader.IsDBNull(8) ? null : ToDateTimeOffset(reader.GetDateTime(8)));

    private static PlatformEnvironment ReadEnvironment(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetGuid(2),
        reader.GetString(3),
        reader.GetString(4),
        (PlatformEnvironmentType)reader.GetInt16(5),
        reader.GetBoolean(6),
        (PlatformResourceStatus)reader.GetInt16(7),
        reader.GetInt64(8),
        ToDateTimeOffset(reader.GetDateTime(9)),
        ToDateTimeOffset(reader.GetDateTime(10)),
        reader.IsDBNull(11) ? null : ToDateTimeOffset(reader.GetDateTime(11)));

    private static PlatformTenantMembership ReadMembership(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        (PlatformMembershipStatus)reader.GetInt16(2),
        reader.GetInt64(3),
        ToDateTimeOffset(reader.GetDateTime(4)),
        ToDateTimeOffset(reader.GetDateTime(5)));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static PlatformPage<T> TrimPage<T>(List<T> items, int limit)
    {
        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new PlatformPage<T>(items, hasMore);
    }
}
