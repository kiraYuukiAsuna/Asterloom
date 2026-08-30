using Asterloom.Modules.Analytics.Model;
using Asterloom.Modules.Analytics.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Analytics;

internal sealed class PostgreSqlAnalyticsStore(NpgsqlDataSource dataSource) : IAnalyticsStore
{
    private const string SchemaColumns =
        "id, tenant_id, application_id, environment_id, key, display_name, " +
        "description, schema_json::text, status, retention_days, version, " +
        "created_at, updated_at, archived_at";
    private const string WriteKeyColumns =
        "id, tenant_id, application_id, environment_id, name, prefix, secret_hash, " +
        "status, version, created_at, updated_at, last_used_at, revoked_at";
    private const string EventColumns =
        "id, event_id, tenant_id, application_id, environment_id, event_schema_id, " +
        "event_name, schema_version, occurred_at, received_at, actor_id, anonymous_id, " +
        "session_id, properties_json::text, context_json::text, sdk_name, sdk_version, " +
        "write_key_prefix";

    public async Task<AnalyticsStorePage<EventSchema>> ListEventSchemasAsync(
        AnalyticsScope scope,
        AnalyticsPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {SchemaColumns}
            FROM analytics.event_schemas
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND (@include_inactive OR status = 1)
              AND (@query = '' OR key ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%'
                   OR description ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset
            LIMIT @limit;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("include_inactive", request.IncludeInactive);
        command.Parameters.AddWithValue("query", request.Query);
        command.Parameters.AddWithValue("offset", request.Offset);
        command.Parameters.AddWithValue("limit", request.PageSize + 1);
        return await ReadPageAsync(command, request.PageSize, ReadSchema, cancellationToken);
    }

    public async Task<EventSchema?> GetEventSchemaAsync(
        AnalyticsScope scope,
        Guid eventSchemaId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {SchemaColumns}
            FROM analytics.event_schemas
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND id = @id;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("id", eventSchemaId);
        return await ReadOneAsync(command, ReadSchema, cancellationToken);
    }

    public async Task<EventSchema?> GetActiveEventSchemaByKeyAsync(
        AnalyticsScope scope,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {SchemaColumns}
            FROM analytics.event_schemas
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND key = @key
              AND status = 1;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("key", key);
        return await ReadOneAsync(command, ReadSchema, cancellationToken);
    }

    public async Task<bool> TryCreateEventSchemaAsync(
        EventSchema schema,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO analytics.event_schemas (
                id, tenant_id, application_id, environment_id, key, display_name,
                description, schema_json, status, retention_days, version,
                created_at, updated_at, archived_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @key, @display_name,
                @description, @schema_json, @status, @retention_days, @version,
                @created_at, @updated_at, @archived_at)
            ON CONFLICT DO NOTHING;
            """);
        AddSchemaParameters(command, schema);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateEventSchemaAsync(
        EventSchema schema,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE analytics.event_schemas
            SET display_name = @display_name,
                description = @description,
                schema_json = @schema_json,
                status = @status,
                retention_days = @retention_days,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE id = @id
              AND tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND version = @expected_version;
            """);
        AddSchemaParameters(command, schema);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<AnalyticsWriteKey>> ListWriteKeysAsync(
        AnalyticsScope scope,
        bool includeRevoked,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {WriteKeyColumns}
            FROM analytics.write_keys
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND (@include_revoked OR status = 1)
            ORDER BY lower(name), id;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("include_revoked", includeRevoked);
        return await ReadManyAsync(command, ReadWriteKey, cancellationToken);
    }

    public async Task<AnalyticsWriteKey?> GetWriteKeyAsync(
        AnalyticsScope scope,
        Guid writeKeyId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {WriteKeyColumns}
            FROM analytics.write_keys
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND id = @id;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("id", writeKeyId);
        return await ReadOneAsync(command, ReadWriteKey, cancellationToken);
    }

    public async Task<AnalyticsWriteKey?> GetWriteKeyByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {WriteKeyColumns} FROM analytics.write_keys WHERE prefix = @prefix;");
        command.Parameters.AddWithValue("prefix", prefix);
        return await ReadOneAsync(command, ReadWriteKey, cancellationToken);
    }

    public async Task<bool> TryCreateWriteKeyAsync(
        AnalyticsWriteKey writeKey,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO analytics.write_keys (
                id, tenant_id, application_id, environment_id, name, prefix, secret_hash,
                status, version, created_at, updated_at, last_used_at, revoked_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @name, @prefix, @secret_hash,
                @status, @version, @created_at, @updated_at, @last_used_at, @revoked_at)
            ON CONFLICT DO NOTHING;
            """);
        AddWriteKeyParameters(command, writeKey);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateWriteKeyAsync(
        AnalyticsWriteKey writeKey,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE analytics.write_keys
            SET name = @name,
                secret_hash = @secret_hash,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                last_used_at = @last_used_at,
                revoked_at = @revoked_at
            WHERE id = @id
              AND tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND version = @expected_version;
            """);
        AddWriteKeyParameters(command, writeKey);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task TouchWriteKeyAsync(
        Guid writeKeyId,
        DateTimeOffset lastUsedAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE analytics.write_keys
            SET last_used_at = @last_used_at,
                updated_at = @last_used_at
            WHERE id = @id AND status = 1;
            """);
        command.Parameters.AddWithValue("id", writeKeyId);
        command.Parameters.AddWithValue("last_used_at", lastUsedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AnalyticsAppendOutcome> AppendEventAsync(
        StoredAnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var deduplication = connection.CreateCommand())
        {
            deduplication.Transaction = transaction;
            deduplication.CommandText =
                """
                INSERT INTO analytics.event_deduplication (
                    tenant_id, application_id, environment_id, event_id,
                    analytics_event_id, received_at)
                VALUES (
                    @tenant_id, @application_id, @environment_id, @event_id,
                    @analytics_event_id, @received_at)
                ON CONFLICT DO NOTHING;
                """;
            AddScope(deduplication, analyticsEvent.Scope);
            deduplication.Parameters.AddWithValue("event_id", analyticsEvent.EventId);
            deduplication.Parameters.AddWithValue("analytics_event_id", analyticsEvent.Id);
            deduplication.Parameters.AddWithValue("received_at", analyticsEvent.ReceivedAt.UtcDateTime);
            if (await deduplication.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AnalyticsAppendOutcome.Deduplicated;
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO analytics.events (
                    id, event_id, tenant_id, application_id, environment_id,
                    event_schema_id, event_name, schema_version, occurred_at, received_at,
                    actor_id, anonymous_id, session_id, properties_json, context_json,
                    sdk_name, sdk_version, write_key_prefix)
                VALUES (
                    @id, @event_id, @tenant_id, @application_id, @environment_id,
                    @event_schema_id, @event_name, @schema_version, @occurred_at, @received_at,
                    @actor_id, @anonymous_id, @session_id, @properties_json, @context_json,
                    @sdk_name, @sdk_version, @write_key_prefix);
                """;
            AddEventParameters(command, analyticsEvent);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return AnalyticsAppendOutcome.Accepted;
    }

    public async Task<AnalyticsStorePage<StoredAnalyticsEvent>> ListEventsAsync(
        AnalyticsScope scope,
        AnalyticsEventFilter filter,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {EventColumns}
            FROM analytics.events
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND (@event_name = '' OR event_name = @event_name)
              AND (@actor_id = '' OR actor_id = @actor_id OR anonymous_id = @actor_id)
              AND (@event_id = '' OR event_id = @event_id)
              AND (@from_at IS NULL OR occurred_at >= @from_at)
              AND (@to_at IS NULL OR occurred_at <= @to_at)
            ORDER BY received_at DESC, id DESC
            OFFSET @offset
            LIMIT @limit;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("event_name", filter.EventName);
        command.Parameters.AddWithValue("actor_id", filter.ActorId);
        command.Parameters.AddWithValue("event_id", filter.EventId);
        AddNullableTimestamp(command, "from_at", filter.FromAt);
        AddNullableTimestamp(command, "to_at", filter.ToAt);
        command.Parameters.AddWithValue("offset", filter.Offset);
        command.Parameters.AddWithValue("limit", filter.PageSize + 1);
        return await ReadPageAsync(command, filter.PageSize, ReadEvent, cancellationToken);
    }

    public async Task<StoredAnalyticsEvent?> GetEventAsync(
        AnalyticsScope scope,
        Guid analyticsEventId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {EventColumns}
            FROM analytics.events
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND id = @id
            LIMIT 1;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("id", analyticsEventId);
        return await ReadOneAsync(command, ReadEvent, cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsAggregationBucket>> AggregateAsync(
        AnalyticsAggregationQuery query,
        CancellationToken cancellationToken)
    {
        var interval = query.Interval switch
        {
            AnalyticsInterval.Hour => "hour",
            AnalyticsInterval.Day => "day",
            AnalyticsInterval.Week => "week",
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
        };
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT date_trunc('{interval}', occurred_at) AS period_start,
                   event_name,
                   count(*) AS event_count,
                   count(DISTINCT NULLIF(
                       CASE WHEN actor_id <> '' THEN actor_id ELSE anonymous_id END, ''))
                       AS unique_actors
            FROM analytics.events
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND occurred_at >= @from_at
              AND occurred_at <= @to_at
              AND (cardinality(@event_names) = 0 OR event_name = ANY(@event_names))
            GROUP BY period_start, event_name
            ORDER BY period_start, event_name;
            """);
        AddScope(command, query.Scope);
        command.Parameters.AddWithValue("from_at", query.FromAt.UtcDateTime);
        command.Parameters.AddWithValue("to_at", query.ToAt.UtcDateTime);
        command.Parameters.AddWithValue(
            "event_names",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            query.EventNames.ToArray());
        var result = new List<AnalyticsAggregationBucket>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                ReadTimestamp(reader, 0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return result;
    }

    public async Task<int> PurgeExpiredEventsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var deduplication = connection.CreateCommand())
        {
            deduplication.Transaction = transaction;
            deduplication.CommandText =
                """
                DELETE FROM analytics.event_deduplication AS dedup
                USING analytics.event_schemas AS schema
                WHERE dedup.tenant_id = schema.tenant_id
                  AND dedup.application_id = schema.application_id
                  AND dedup.environment_id = schema.environment_id
                  AND dedup.received_at < @now
                      - make_interval(days => schema.retention_days);
                """;
            deduplication.Parameters.AddWithValue("now", now.UtcDateTime);
            await deduplication.ExecuteNonQueryAsync(cancellationToken);
        }

        int deleted;
        await using (var events = connection.CreateCommand())
        {
            events.Transaction = transaction;
            events.CommandText =
                """
                DELETE FROM analytics.events AS event
                USING analytics.event_schemas AS schema
                WHERE event.event_schema_id = schema.id
                  AND event.received_at < @now
                      - make_interval(days => schema.retention_days);
                """;
            events.Parameters.AddWithValue("now", now.UtcDateTime);
            deleted = await events.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private static void AddScope(NpgsqlCommand command, AnalyticsScope scope)
    {
        command.Parameters.AddWithValue("tenant_id", scope.TenantId);
        command.Parameters.AddWithValue("application_id", scope.ApplicationId);
        command.Parameters.AddWithValue("environment_id", scope.EnvironmentId);
    }

    private static void AddSchemaParameters(NpgsqlCommand command, EventSchema schema)
    {
        command.Parameters.AddWithValue("id", schema.Id);
        AddScope(command, schema.Scope);
        command.Parameters.AddWithValue("key", schema.Key);
        command.Parameters.AddWithValue("display_name", schema.DisplayName);
        command.Parameters.AddWithValue("description", schema.Description);
        command.Parameters.AddWithValue("schema_json", NpgsqlDbType.Jsonb, schema.SchemaJson);
        command.Parameters.AddWithValue("status", (short)schema.Status);
        command.Parameters.AddWithValue("retention_days", schema.RetentionDays);
        command.Parameters.AddWithValue("version", schema.Version);
        command.Parameters.AddWithValue("created_at", schema.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", schema.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "archived_at", schema.ArchivedAt);
    }

    private static void AddWriteKeyParameters(NpgsqlCommand command, AnalyticsWriteKey writeKey)
    {
        command.Parameters.AddWithValue("id", writeKey.Id);
        AddScope(command, writeKey.Scope);
        command.Parameters.AddWithValue("name", writeKey.Name);
        command.Parameters.AddWithValue("prefix", writeKey.Prefix);
        command.Parameters.AddWithValue("secret_hash", NpgsqlDbType.Bytea, writeKey.SecretHash);
        command.Parameters.AddWithValue("status", (short)writeKey.Status);
        command.Parameters.AddWithValue("version", writeKey.Version);
        command.Parameters.AddWithValue("created_at", writeKey.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", writeKey.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "last_used_at", writeKey.LastUsedAt);
        AddNullableTimestamp(command, "revoked_at", writeKey.RevokedAt);
    }

    private static void AddEventParameters(
        NpgsqlCommand command,
        StoredAnalyticsEvent analyticsEvent)
    {
        command.Parameters.AddWithValue("id", analyticsEvent.Id);
        command.Parameters.AddWithValue("event_id", analyticsEvent.EventId);
        AddScope(command, analyticsEvent.Scope);
        command.Parameters.AddWithValue("event_schema_id", analyticsEvent.EventSchemaId);
        command.Parameters.AddWithValue("event_name", analyticsEvent.EventName);
        command.Parameters.AddWithValue("schema_version", analyticsEvent.SchemaVersion);
        command.Parameters.AddWithValue("occurred_at", analyticsEvent.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("received_at", analyticsEvent.ReceivedAt.UtcDateTime);
        command.Parameters.AddWithValue("actor_id", analyticsEvent.ActorId);
        command.Parameters.AddWithValue("anonymous_id", analyticsEvent.AnonymousId);
        command.Parameters.AddWithValue("session_id", analyticsEvent.SessionId);
        command.Parameters.AddWithValue(
            "properties_json",
            NpgsqlDbType.Jsonb,
            analyticsEvent.PropertiesJson);
        command.Parameters.AddWithValue(
            "context_json",
            NpgsqlDbType.Jsonb,
            analyticsEvent.ContextJson);
        command.Parameters.AddWithValue("sdk_name", analyticsEvent.SdkName);
        command.Parameters.AddWithValue("sdk_version", analyticsEvent.SdkVersion);
        command.Parameters.AddWithValue("write_key_prefix", analyticsEvent.WriteKeyPrefix);
    }

    private static EventSchema ReadSchema(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        new AnalyticsScope(reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3)),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        (AnalyticsResourceStatus)reader.GetInt16(8),
        reader.GetInt32(9),
        reader.GetInt64(10),
        ReadTimestamp(reader, 11),
        ReadTimestamp(reader, 12),
        ReadNullableTimestamp(reader, 13));

    private static AnalyticsWriteKey ReadWriteKey(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        new AnalyticsScope(reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3)),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetFieldValue<byte[]>(6),
        (AnalyticsWriteKeyStatus)reader.GetInt16(7),
        reader.GetInt64(8),
        ReadTimestamp(reader, 9),
        ReadTimestamp(reader, 10),
        ReadNullableTimestamp(reader, 11),
        ReadNullableTimestamp(reader, 12));

    private static StoredAnalyticsEvent ReadEvent(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        new AnalyticsScope(reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4)),
        reader.GetGuid(5),
        reader.GetString(6),
        reader.GetInt64(7),
        ReadTimestamp(reader, 8),
        ReadTimestamp(reader, 9),
        reader.GetString(10),
        reader.GetString(11),
        reader.GetString(12),
        reader.GetString(13),
        reader.GetString(14),
        reader.GetString(15),
        reader.GetString(16),
        reader.GetString(17));

    private static async Task<T?> ReadOneAsync<T>(
        NpgsqlCommand command,
        Func<NpgsqlDataReader, T> mapper,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? mapper(reader) : default;
    }

    private static async Task<IReadOnlyList<T>> ReadManyAsync<T>(
        NpgsqlCommand command,
        Func<NpgsqlDataReader, T> mapper,
        CancellationToken cancellationToken)
    {
        var result = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(mapper(reader));
        }

        return result;
    }

    private static async Task<AnalyticsStorePage<T>> ReadPageAsync<T>(
        NpgsqlCommand command,
        int pageSize,
        Func<NpgsqlDataReader, T> mapper,
        CancellationToken cancellationToken)
    {
        var items = (await ReadManyAsync(command, mapper, cancellationToken)).ToList();
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new(items, hasMore);
    }

    private static DateTimeOffset ReadTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static DateTimeOffset? ReadNullableTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadTimestamp(reader, ordinal);

    private static void AddNullableTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset? value) => command.Parameters.Add(
        new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
        {
            Value = value is null ? DBNull.Value : value.Value.UtcDateTime,
        });
}
