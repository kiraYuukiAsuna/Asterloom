using System.Data.Common;
using Asterloom.Modules.Outbox;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Outbox;

internal sealed class PostgreSqlOutboxStore(NpgsqlDataSource dataSource) : IOutboxStore
{
    private const string SelectColumns =
        """
        id, event_type, schema_version, payload::text, correlation_id,
        tenant_id, application_id, environment_id, occurred_at, available_at,
        attempt_count, locked_by, locked_until, processed_at, dead_lettered_at,
        last_error
        """;

    private const string QualifiedSelectColumns =
        """
        message.id, message.event_type, message.schema_version, message.payload::text,
        message.correlation_id, message.tenant_id, message.application_id,
        message.environment_id, message.occurred_at, message.available_at,
        message.attempt_count, message.locked_by, message.locked_until,
        message.processed_at, message.dead_lettered_at, message.last_error
        """;

    public async Task EnqueueAsync(
        OutboxMessageDraft message,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateDraft(message);
        await using var command = dataSource.CreateCommand(CreateInsertSql());
        AddDraftParameters(command, message);
        await ExecuteInsertAsync(command, message.Id, cancellationToken);
    }

    public async Task EnqueueAsync(
        OutboxMessageDraft message,
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateDraft(message);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection is not NpgsqlConnection postgresConnection
            || transaction is not NpgsqlTransaction postgresTransaction
            || !ReferenceEquals(postgresTransaction.Connection, postgresConnection))
        {
            throw new ArgumentException(
                "A matching PostgreSQL connection and transaction are required.",
                nameof(transaction));
        }

        await using var command = postgresConnection.CreateCommand();
        command.Transaction = postgresTransaction;
        command.CommandText = CreateInsertSql();
        AddDraftParameters(command, message);
        await ExecuteInsertAsync(command, message.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        string workerId,
        IReadOnlyCollection<string> eventTypes,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateWorkerId(workerId);
        var types = OutboxValidation.ValidateEventTypes(eventTypes);
        OutboxValidation.ValidateClaim(leaseDuration, maximumCount);
        await using var command = dataSource.CreateCommand(
            $$"""
            WITH candidates AS (
                SELECT id
                FROM infrastructure.outbox_messages
                WHERE event_type = ANY (@event_types)
                  AND processed_at IS NULL
                  AND dead_lettered_at IS NULL
                  AND available_at <= @now
                  AND (locked_until IS NULL OR locked_until <= @now)
                ORDER BY available_at, occurred_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT @maximum_count
            )
            UPDATE infrastructure.outbox_messages AS message
            SET locked_by = @worker_id,
                locked_until = @locked_until,
                attempt_count = message.attempt_count + 1
            FROM candidates
            WHERE message.id = candidates.id
            RETURNING {{QualifiedSelectColumns}};
            """);
        command.Parameters.Add(
            new NpgsqlParameter<string[]>("event_types", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                TypedValue = types,
            });
        command.Parameters.AddWithValue("worker_id", workerId);
        command.Parameters.AddWithValue("now", now.UtcDateTime);
        command.Parameters.AddWithValue("locked_until", now.Add(leaseDuration).UtcDateTime);
        command.Parameters.AddWithValue("maximum_count", maximumCount);
        var claimed = new List<OutboxMessage>(maximumCount);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claimed.Add(Read(reader));
        }

        return claimed;
    }

    public async Task<bool> HasReceiptAsync(
        Guid eventId,
        string consumerName,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateConsumerName(consumerName);
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM infrastructure.inbox_receipts
                WHERE event_id = @event_id AND consumer_name = @consumer_name);
            """);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("consumer_name", consumerName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> RecordReceiptAsync(
        Guid eventId,
        string consumerName,
        string workerId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateConsumerName(consumerName);
        OutboxValidation.ValidateWorkerId(workerId);
        await using var command = dataSource.CreateCommand(
            """
            WITH owned AS (
                SELECT id
                FROM infrastructure.outbox_messages
                WHERE id = @event_id
                  AND locked_by = @worker_id
                  AND processed_at IS NULL
                  AND dead_lettered_at IS NULL
            ), inserted AS (
                INSERT INTO infrastructure.inbox_receipts (
                    event_id, consumer_name, processed_at)
                SELECT id, @consumer_name, @processed_at
                FROM owned
                ON CONFLICT DO NOTHING
            )
            SELECT EXISTS (SELECT 1 FROM owned);
            """);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("consumer_name", consumerName);
        command.Parameters.AddWithValue("worker_id", workerId);
        command.Parameters.AddWithValue("processed_at", processedAt.UtcDateTime);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> MarkProcessedAsync(
        Guid eventId,
        string workerId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateWorkerId(workerId);
        await using var command = dataSource.CreateCommand(
            """
            UPDATE infrastructure.outbox_messages
            SET processed_at = @processed_at,
                locked_by = NULL,
                locked_until = NULL,
                last_error = ''
            WHERE id = @event_id
              AND locked_by = @worker_id
              AND processed_at IS NULL
              AND dead_lettered_at IS NULL;
            """);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("worker_id", workerId);
        command.Parameters.AddWithValue("processed_at", processedAt.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> MarkFailedAsync(
        Guid eventId,
        string workerId,
        DateTimeOffset availableAt,
        string errorCode,
        DateTimeOffset? deadLetteredAt,
        CancellationToken cancellationToken)
    {
        OutboxValidation.ValidateWorkerId(workerId);
        await using var command = dataSource.CreateCommand(
            """
            UPDATE infrastructure.outbox_messages
            SET available_at = @available_at,
                locked_by = NULL,
                locked_until = NULL,
                last_error = @last_error,
                dead_lettered_at = @dead_lettered_at
            WHERE id = @event_id
              AND locked_by = @worker_id
              AND processed_at IS NULL
              AND dead_lettered_at IS NULL;
            """);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("worker_id", workerId);
        command.Parameters.AddWithValue("available_at", availableAt.UtcDateTime);
        command.Parameters.AddWithValue("last_error", NormalizeError(errorCode));
        AddNullableTimestamp(command, "dead_lettered_at", deadLetteredAt);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<OutboxMessage?> GetAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM infrastructure.outbox_messages WHERE id = @id;");
        command.Parameters.AddWithValue("id", eventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static string CreateInsertSql() =>
        """
        INSERT INTO infrastructure.outbox_messages (
            id, event_type, schema_version, payload, correlation_id,
            tenant_id, application_id, environment_id, occurred_at, available_at)
        VALUES (
            @id, @event_type, @schema_version, @payload::jsonb, @correlation_id,
            @tenant_id, @application_id, @environment_id, @occurred_at, @available_at);
        """;

    private static async Task ExecuteInsertAsync(
        NpgsqlCommand command,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException(
                $"Outbox event '{eventId}' already exists.",
                exception);
        }
    }

    private static void AddDraftParameters(NpgsqlCommand command, OutboxMessageDraft message)
    {
        command.Parameters.AddWithValue("id", message.Id);
        command.Parameters.AddWithValue("event_type", message.EventType);
        command.Parameters.AddWithValue("schema_version", message.SchemaVersion);
        command.Parameters.AddWithValue("payload", message.Payload);
        command.Parameters.AddWithValue("correlation_id", message.CorrelationId);
        AddNullableGuid(command, "tenant_id", message.TenantId);
        AddNullableGuid(command, "application_id", message.ApplicationId);
        AddNullableGuid(command, "environment_id", message.EnvironmentId);
        command.Parameters.AddWithValue("occurred_at", message.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("available_at", message.AvailableAt.UtcDateTime);
    }

    private static OutboxMessage Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetInt32(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetGuid(5),
        reader.IsDBNull(6) ? null : reader.GetGuid(6),
        reader.IsDBNull(7) ? null : reader.GetGuid(7),
        ToDateTimeOffset(reader.GetDateTime(8)),
        ToDateTimeOffset(reader.GetDateTime(9)),
        reader.GetInt32(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : ToDateTimeOffset(reader.GetDateTime(12)),
        reader.IsDBNull(13) ? null : ToDateTimeOffset(reader.GetDateTime(13)),
        reader.IsDBNull(14) ? null : ToDateTimeOffset(reader.GetDateTime(14)),
        reader.GetString(15));

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

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string NormalizeError(string value) =>
        string.IsNullOrWhiteSpace(value) ? "handler_failure" : value[..Math.Min(value.Length, 200)];
}
