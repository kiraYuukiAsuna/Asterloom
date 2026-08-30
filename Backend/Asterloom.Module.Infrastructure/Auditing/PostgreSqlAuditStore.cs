using Asterloom.Modules.Auditing;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Auditing;

internal sealed class PostgreSqlAuditStore(NpgsqlDataSource dataSource) : IAuditStore
{
    public async Task AppendAsync(
        AsterloomAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO infrastructure.audit_events (
                id, actor_id, tenant_id, application_id, environment_id,
                operation, resource_type, resource_id, request_id, outcome,
                error_code, change_summary, created_at)
            VALUES (
                @id, @actor_id, @tenant_id, @application_id, @environment_id,
                @operation, @resource_type, @resource_id, @request_id, @outcome,
                @error_code, @change_summary, @created_at);
            """);
        AddParameters(command, auditEvent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AuditPage> ListAsync(
        AuditPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, actor_id, tenant_id, application_id, environment_id,
                   operation, resource_type, resource_id, request_id, outcome,
                   error_code, change_summary, created_at
            FROM infrastructure.audit_events
            WHERE (@actor_id = '' OR actor_id ILIKE '%' || @actor_id || '%')
              AND (@operation = '' OR operation ILIKE '%' || @operation || '%')
              AND (@outcome IS NULL OR outcome = @outcome)
              AND (@request_id = '' OR request_id = @request_id)
              AND (@from_at IS NULL OR created_at >= @from_at)
              AND (@to_at IS NULL OR created_at <= @to_at)
            ORDER BY created_at DESC, id DESC
            OFFSET @offset
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("actor_id", request.ActorId);
        command.Parameters.AddWithValue("operation", request.Operation);
        AddNullableInt16(
            command,
            "outcome",
            request.Outcome is null ? null : (short)request.Outcome.Value);
        command.Parameters.AddWithValue("request_id", request.RequestId);
        AddNullableTimestamp(command, "from_at", request.FromAt);
        AddNullableTimestamp(command, "to_at", request.ToAt);
        command.Parameters.AddWithValue("offset", request.Offset);
        command.Parameters.AddWithValue("limit", request.PageSize + 1);
        var items = new List<AsterloomAuditEvent>(request.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Read(reader));
        }

        var hasMore = items.Count > request.PageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new AuditPage(items, hasMore);
    }

    public async Task<AsterloomAuditEvent?> GetAsync(
        Guid auditEventId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, actor_id, tenant_id, application_id, environment_id,
                   operation, resource_type, resource_id, request_id, outcome,
                   error_code, change_summary, created_at
            FROM infrastructure.audit_events
            WHERE id = @id;
            """);
        command.Parameters.AddWithValue("id", auditEventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static void AddParameters(NpgsqlCommand command, AsterloomAuditEvent auditEvent)
    {
        command.Parameters.AddWithValue("id", auditEvent.Id);
        command.Parameters.AddWithValue("actor_id", auditEvent.ActorId);
        AddNullableGuid(command, "tenant_id", auditEvent.TenantId);
        AddNullableGuid(command, "application_id", auditEvent.ApplicationId);
        AddNullableGuid(command, "environment_id", auditEvent.EnvironmentId);
        command.Parameters.AddWithValue("operation", auditEvent.Operation);
        command.Parameters.AddWithValue("resource_type", auditEvent.ResourceType);
        command.Parameters.AddWithValue("resource_id", auditEvent.ResourceId);
        command.Parameters.AddWithValue("request_id", auditEvent.RequestId);
        command.Parameters.AddWithValue("outcome", (short)auditEvent.Outcome);
        command.Parameters.AddWithValue("error_code", auditEvent.ErrorCode);
        command.Parameters.AddWithValue("change_summary", auditEvent.ChangeSummary);
        command.Parameters.AddWithValue("created_at", auditEvent.CreatedAt.UtcDateTime);
    }

    private static AsterloomAuditEvent Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetGuid(2),
        reader.IsDBNull(3) ? null : reader.GetGuid(3),
        reader.IsDBNull(4) ? null : reader.GetGuid(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        (AuditOutcome)reader.GetInt16(9),
        reader.GetString(10),
        reader.GetString(11),
        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(12), DateTimeKind.Utc)));

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.Uuid)
            {
                Value = value is null ? DBNull.Value : value.Value,
            });

    private static void AddNullableInt16(NpgsqlCommand command, string name, short? value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.Smallint)
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
}
