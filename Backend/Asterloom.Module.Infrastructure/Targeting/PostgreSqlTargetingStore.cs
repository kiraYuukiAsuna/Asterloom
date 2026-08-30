using System.Text.Json;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;
using Asterloom.Targeting;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Targeting;

internal sealed class PostgreSqlTargetingStore(NpgsqlDataSource dataSource) : ITargetingStore
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<TargetingStorePage<TargetingSegment>> ListSegmentsAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        TargetingPageRequest page,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, tenant_id, application_id, environment_id, key, display_name,
                   description, rule::text, status, version, created_at, updated_at, archived_at
            FROM targeting.segments
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND (@include_archived OR status = 1)
              AND (@query = ''
                   OR key ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%'
                   OR description ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset
            LIMIT @limit;
            """);
        AddScopeParameters(command, tenantId, applicationId, environmentId);
        command.Parameters.AddWithValue("include_archived", page.IncludeArchived);
        command.Parameters.AddWithValue("query", page.Query);
        command.Parameters.AddWithValue("offset", page.Offset);
        command.Parameters.AddWithValue("limit", page.PageSize + 1);
        var items = new List<TargetingSegment>(page.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadSegment(reader));
        }

        var hasMore = items.Count > page.PageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new TargetingStorePage<TargetingSegment>(items, hasMore);
    }

    public async Task<TargetingSegment?> GetSegmentAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        Guid segmentId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, tenant_id, application_id, environment_id, key, display_name,
                   description, rule::text, status, version, created_at, updated_at, archived_at
            FROM targeting.segments
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND id = @id;
            """);
        AddScopeParameters(command, tenantId, applicationId, environmentId);
        command.Parameters.AddWithValue("id", segmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSegment(reader) : null;
    }

    public async Task<bool> TryCreateSegmentAsync(
        TargetingSegment segment,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO targeting.segments (
                id, tenant_id, application_id, environment_id, key, display_name,
                description, rule, status, version, created_at, updated_at, archived_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @key, @display_name,
                @description, @rule, @status, @version, @created_at, @updated_at, @archived_at)
            ON CONFLICT DO NOTHING;
            """);
        AddSegmentParameters(command, segment);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateSegmentAsync(
        TargetingSegment segment,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE targeting.segments
            SET display_name = @display_name,
                description = @description,
                rule = @rule,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE id = @id
              AND tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND key = @key
              AND version = @expected_version;
            """);
        AddSegmentParameters(command, segment);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void AddSegmentParameters(NpgsqlCommand command, TargetingSegment segment)
    {
        command.Parameters.AddWithValue("id", segment.Id);
        AddScopeParameters(
            command,
            segment.TenantId,
            segment.ApplicationId,
            segment.EnvironmentId);
        command.Parameters.AddWithValue("key", segment.Key);
        command.Parameters.AddWithValue("display_name", segment.DisplayName);
        command.Parameters.AddWithValue("description", segment.Description);
        command.Parameters.Add(
            new NpgsqlParameter("rule", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(segment.Rule, SerializerOptions),
            });
        command.Parameters.AddWithValue("status", (short)segment.Status);
        command.Parameters.AddWithValue("version", segment.Version);
        command.Parameters.AddWithValue("created_at", segment.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", segment.UpdatedAt.UtcDateTime);
        command.Parameters.Add(
            new NpgsqlParameter("archived_at", NpgsqlDbType.TimestampTz)
            {
                Value = segment.ArchivedAt is null
                    ? DBNull.Value
                    : segment.ArchivedAt.Value.UtcDateTime,
            });
    }

    private static void AddScopeParameters(
        NpgsqlCommand command,
        Guid tenantId,
        Guid applicationId,
        Guid environmentId)
    {
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("application_id", applicationId);
        command.Parameters.AddWithValue("environment_id", environmentId);
    }

    private static TargetingSegment ReadSegment(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetGuid(2),
        reader.GetGuid(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        JsonSerializer.Deserialize<TargetingRule>(reader.GetString(7), SerializerOptions)
            ?? throw new InvalidOperationException("A targeting segment contains an empty rule."),
        (TargetingResourceStatus)reader.GetInt16(8),
        reader.GetInt64(9),
        ToDateTimeOffset(reader.GetDateTime(10)),
        ToDateTimeOffset(reader.GetDateTime(11)),
        reader.IsDBNull(12) ? null : ToDateTimeOffset(reader.GetDateTime(12)));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
