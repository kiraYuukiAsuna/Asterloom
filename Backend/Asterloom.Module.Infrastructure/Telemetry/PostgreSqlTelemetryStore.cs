using Asterloom.Modules.Telemetry.Model;
using Asterloom.Modules.Telemetry.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Telemetry;

internal sealed class PostgreSqlTelemetryStore(NpgsqlDataSource dataSource) : ITelemetryStore
{
    private const string SourceColumns =
        "id, tenant_id, application_id, environment_id, key, display_name, " +
        "description, service_name, resource_attributes_json::text, status, version, " +
        "created_at, updated_at, archived_at";
    private const string SettingsColumns =
        "tenant_id, application_id, environment_id, sampling_ratio, traces_enabled, " +
        "metrics_enabled, logs_enabled, exporter_endpoint, exporter_protocol, " +
        "diagnostics_base_url, version, updated_at";
    private const string ErrorColumns =
        "id, tenant_id, application_id, environment_id, service_name, exception_type, " +
        "message, grpc_method, trace_id, span_id, request_id, occurred_at";
    private const string RecordColumns =
        "id, tenant_id, application_id, environment_id, signal_type, service_name, " +
        "observed_at, trace_id, span_id, name, category, value, duration_milliseconds, " +
        "attributes_json::text, payload_json::text, created_at";

    public async Task<TelemetryStorePage<TelemetrySource>> ListSourcesAsync(
        TelemetryScope scope,
        TelemetryPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {SourceColumns}
            FROM telemetry.sources
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND (@include_archived OR status = 1)
              AND (@query = '' OR key ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%'
                   OR description ILIKE '%' || @query || '%'
                   OR service_name ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset
            LIMIT @limit;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("include_archived", request.IncludeArchived);
        command.Parameters.AddWithValue("query", request.Query);
        command.Parameters.AddWithValue("offset", request.Offset);
        command.Parameters.AddWithValue("limit", request.PageSize + 1);
        return await ReadPageAsync(command, request.PageSize, ReadSource, cancellationToken);
    }

    public async Task<TelemetrySource?> GetSourceAsync(
        TelemetryScope scope,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {SourceColumns}
            FROM telemetry.sources
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND id = @id;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("id", sourceId);
        return await ReadOneAsync(command, ReadSource, cancellationToken);
    }

    public async Task<bool> HasActiveSourceAsync(
        TelemetryScope scope,
        string serviceName,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM telemetry.sources
                WHERE tenant_id = @tenant_id
                  AND application_id = @application_id
                  AND environment_id = @environment_id
                  AND service_name = @service_name
                  AND status = 1);
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("service_name", serviceName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> TryCreateSourceAsync(
        TelemetrySource source,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO telemetry.sources (
                id, tenant_id, application_id, environment_id, key, display_name,
                description, service_name, resource_attributes_json, status, version,
                created_at, updated_at, archived_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @key, @display_name,
                @description, @service_name, @resource_attributes_json, @status, @version,
                @created_at, @updated_at, @archived_at)
            ON CONFLICT DO NOTHING;
            """);
        AddSourceParameters(command, source);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateSourceAsync(
        TelemetrySource source,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE telemetry.sources
            SET display_name = @display_name,
                description = @description,
                service_name = @service_name,
                resource_attributes_json = @resource_attributes_json,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE id = @id
              AND tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND version = @expected_version;
            """);
        AddSourceParameters(command, source);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public async Task<TelemetrySettings?> GetSettingsAsync(
        TelemetryScope scope,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {SettingsColumns}
            FROM telemetry.settings
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id;
            """);
        AddScope(command, scope);
        return await ReadOneAsync(command, ReadSettings, cancellationToken);
    }

    public async Task<bool> TryUpsertSettingsAsync(
        TelemetrySettings settings,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO telemetry.settings (
                tenant_id, application_id, environment_id, sampling_ratio,
                traces_enabled, metrics_enabled, logs_enabled, exporter_endpoint,
                exporter_protocol, diagnostics_base_url, version, updated_at)
            VALUES (
                @tenant_id, @application_id, @environment_id, @sampling_ratio,
                @traces_enabled, @metrics_enabled, @logs_enabled, @exporter_endpoint,
                @exporter_protocol, @diagnostics_base_url, @version, @updated_at)
            ON CONFLICT (tenant_id, application_id, environment_id) DO UPDATE
            SET sampling_ratio = EXCLUDED.sampling_ratio,
                traces_enabled = EXCLUDED.traces_enabled,
                metrics_enabled = EXCLUDED.metrics_enabled,
                logs_enabled = EXCLUDED.logs_enabled,
                exporter_endpoint = EXCLUDED.exporter_endpoint,
                exporter_protocol = EXCLUDED.exporter_protocol,
                diagnostics_base_url = EXCLUDED.diagnostics_base_url,
                version = EXCLUDED.version,
                updated_at = EXCLUDED.updated_at
            WHERE telemetry.settings.version = @expected_version;
            """);
        AddSettingsParameters(command, settings);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task AppendErrorAsync(
        TelemetryError telemetryError,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO telemetry.recent_errors (
                id, tenant_id, application_id, environment_id, service_name,
                exception_type, message, grpc_method, trace_id, span_id,
                request_id, occurred_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @service_name,
                @exception_type, @message, @grpc_method, @trace_id, @span_id,
                @request_id, @occurred_at)
            ON CONFLICT DO NOTHING;

            DELETE FROM telemetry.recent_errors
            WHERE occurred_at < now() - interval '30 days';
            """);
        AddErrorParameters(command, telemetryError);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TelemetryStorePage<TelemetryError>> ListErrorsAsync(
        TelemetryScope scope,
        TelemetryErrorFilter filter,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {ErrorColumns}
            FROM telemetry.recent_errors
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND (@service_name = '' OR service_name = @service_name)
              AND (@trace_id = '' OR trace_id = @trace_id)
            ORDER BY occurred_at DESC, id DESC
            OFFSET @offset
            LIMIT @limit;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("service_name", filter.ServiceName);
        command.Parameters.AddWithValue("trace_id", filter.TraceId);
        command.Parameters.AddWithValue("offset", filter.Offset);
        command.Parameters.AddWithValue("limit", filter.PageSize + 1);
        return await ReadPageAsync(command, filter.PageSize, ReadError, cancellationToken);
    }

    public async Task AppendRecordsAsync(
        IReadOnlyCollection<TelemetryRecord> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return;
        }

        await using var batch = dataSource.CreateBatch();
        foreach (var record in records)
        {
            var command = new NpgsqlBatchCommand(
                """
                INSERT INTO telemetry.records (
                    id, tenant_id, application_id, environment_id, signal_type,
                    service_name, observed_at, trace_id, span_id, name, category,
                    value, duration_milliseconds, attributes_json, payload_json, created_at)
                VALUES (
                    @id, @tenant_id, @application_id, @environment_id, @signal_type,
                    @service_name, @observed_at, @trace_id, @span_id, @name, @category,
                    @value, @duration_milliseconds, @attributes_json, @payload_json, @created_at)
                ON CONFLICT (id) DO NOTHING;
                """);
            AddRecordParameters(command, record);
            batch.BatchCommands.Add(command);
        }

        batch.BatchCommands.Add(new NpgsqlBatchCommand(
            "DELETE FROM telemetry.records WHERE observed_at < now() - interval '7 days';"));
        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TelemetryStorePage<TelemetryRecord>> ListRecordsAsync(
        TelemetryScope scope,
        TelemetryRecordFilter filter,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {RecordColumns}
            FROM telemetry.records
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND signal_type = @signal_type
              AND (@service_name = '' OR service_name = @service_name)
              AND (@trace_id = '' OR trace_id = @trace_id)
              AND (@query = '' OR name ILIKE '%' || @query || '%'
                   OR category ILIKE '%' || @query || '%'
                   OR value ILIKE '%' || @query || '%'
                   OR attributes_json::text ILIKE '%' || @query || '%')
              AND (@from_at IS NULL OR observed_at >= @from_at)
              AND (@to_at IS NULL OR observed_at <= @to_at)
            ORDER BY observed_at DESC, id DESC
            OFFSET @offset
            LIMIT @limit;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("signal_type", (short)filter.SignalType);
        command.Parameters.AddWithValue("service_name", filter.ServiceName);
        command.Parameters.AddWithValue("trace_id", filter.TraceId);
        command.Parameters.AddWithValue("query", filter.Query);
        AddNullableTimestamp(command, "from_at", filter.FromAt);
        AddNullableTimestamp(command, "to_at", filter.ToAt);
        command.Parameters.AddWithValue("offset", filter.Offset);
        command.Parameters.AddWithValue("limit", filter.PageSize + 1);
        return await ReadPageAsync(command, filter.PageSize, ReadRecord, cancellationToken);
    }

    private static void AddScope(NpgsqlCommand command, TelemetryScope scope)
    {
        command.Parameters.AddWithValue("tenant_id", scope.TenantId);
        command.Parameters.AddWithValue("application_id", scope.ApplicationId);
        command.Parameters.AddWithValue("environment_id", scope.EnvironmentId);
    }

    private static void AddSourceParameters(NpgsqlCommand command, TelemetrySource source)
    {
        command.Parameters.AddWithValue("id", source.Id);
        AddScope(command, source.Scope);
        command.Parameters.AddWithValue("key", source.Key);
        command.Parameters.AddWithValue("display_name", source.DisplayName);
        command.Parameters.AddWithValue("description", source.Description);
        command.Parameters.AddWithValue("service_name", source.ServiceName);
        command.Parameters.AddWithValue(
            "resource_attributes_json",
            NpgsqlDbType.Jsonb,
            source.ResourceAttributesJson);
        command.Parameters.AddWithValue("status", (short)source.Status);
        command.Parameters.AddWithValue("version", source.Version);
        command.Parameters.AddWithValue("created_at", source.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", source.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "archived_at", source.ArchivedAt);
    }

    private static void AddSettingsParameters(
        NpgsqlCommand command,
        TelemetrySettings settings)
    {
        AddScope(command, settings.Scope);
        command.Parameters.AddWithValue("sampling_ratio", settings.SamplingRatio);
        command.Parameters.AddWithValue("traces_enabled", settings.TracesEnabled);
        command.Parameters.AddWithValue("metrics_enabled", settings.MetricsEnabled);
        command.Parameters.AddWithValue("logs_enabled", settings.LogsEnabled);
        command.Parameters.AddWithValue("exporter_endpoint", settings.ExporterEndpoint);
        command.Parameters.AddWithValue("exporter_protocol", (short)settings.ExporterProtocol);
        command.Parameters.AddWithValue("diagnostics_base_url", settings.DiagnosticsBaseUrl);
        command.Parameters.AddWithValue("version", settings.Version);
        command.Parameters.AddWithValue("updated_at", settings.UpdatedAt.UtcDateTime);
    }

    private static void AddErrorParameters(
        NpgsqlCommand command,
        TelemetryError telemetryError)
    {
        command.Parameters.AddWithValue("id", telemetryError.Id);
        AddNullableGuid(command, "tenant_id", telemetryError.Scope?.TenantId);
        AddNullableGuid(command, "application_id", telemetryError.Scope?.ApplicationId);
        AddNullableGuid(command, "environment_id", telemetryError.Scope?.EnvironmentId);
        command.Parameters.AddWithValue("service_name", telemetryError.ServiceName);
        command.Parameters.AddWithValue("exception_type", telemetryError.ExceptionType);
        command.Parameters.AddWithValue("message", telemetryError.Message);
        command.Parameters.AddWithValue("grpc_method", telemetryError.GrpcMethod);
        command.Parameters.AddWithValue("trace_id", telemetryError.TraceId);
        command.Parameters.AddWithValue("span_id", telemetryError.SpanId);
        command.Parameters.AddWithValue("request_id", telemetryError.RequestId);
        command.Parameters.AddWithValue("occurred_at", telemetryError.OccurredAt.UtcDateTime);
    }

    private static void AddRecordParameters(
        NpgsqlBatchCommand command,
        TelemetryRecord record)
    {
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("tenant_id", record.Scope.TenantId);
        command.Parameters.AddWithValue("application_id", record.Scope.ApplicationId);
        command.Parameters.AddWithValue("environment_id", record.Scope.EnvironmentId);
        command.Parameters.AddWithValue("signal_type", (short)record.SignalType);
        command.Parameters.AddWithValue("service_name", record.ServiceName);
        command.Parameters.AddWithValue("observed_at", record.ObservedAt.UtcDateTime);
        command.Parameters.AddWithValue("trace_id", record.TraceId);
        command.Parameters.AddWithValue("span_id", record.SpanId);
        command.Parameters.AddWithValue("name", record.Name);
        command.Parameters.AddWithValue("category", record.Category);
        command.Parameters.AddWithValue("value", record.Value);
        command.Parameters.Add(new NpgsqlParameter("duration_milliseconds", NpgsqlDbType.Double)
        {
            Value = record.DurationMilliseconds is null
                ? DBNull.Value
                : record.DurationMilliseconds.Value,
        });
        command.Parameters.AddWithValue(
            "attributes_json",
            NpgsqlDbType.Jsonb,
            record.AttributesJson);
        command.Parameters.AddWithValue("payload_json", NpgsqlDbType.Jsonb, record.PayloadJson);
        command.Parameters.AddWithValue("created_at", record.CreatedAt.UtcDateTime);
    }

    private static TelemetrySource ReadSource(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        new TelemetryScope(reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3)),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        (TelemetryResourceStatus)reader.GetInt16(9),
        reader.GetInt64(10),
        ReadTimestamp(reader, 11),
        ReadTimestamp(reader, 12),
        ReadNullableTimestamp(reader, 13));

    private static TelemetrySettings ReadSettings(NpgsqlDataReader reader) => new(
        new TelemetryScope(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2)),
        reader.GetDouble(3),
        reader.GetBoolean(4),
        reader.GetBoolean(5),
        reader.GetBoolean(6),
        reader.GetString(7),
        (TelemetryOtlpProtocol)reader.GetInt16(8),
        reader.GetString(9),
        reader.GetInt64(10),
        ReadTimestamp(reader, 11));

    private static TelemetryError ReadError(NpgsqlDataReader reader)
    {
        TelemetryScope? scope = reader.IsDBNull(1)
            ? null
            : new TelemetryScope(reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3));
        return new(
            reader.GetGuid(0),
            scope,
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            ReadTimestamp(reader, 11));
    }

    private static TelemetryRecord ReadRecord(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        new TelemetryScope(reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3)),
        (TelemetrySignalType)reader.GetInt16(4),
        reader.GetString(5),
        ReadTimestamp(reader, 6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetDouble(12),
        reader.GetString(13),
        reader.GetString(14),
        ReadTimestamp(reader, 15));

    private static async Task<T?> ReadOneAsync<T>(
        NpgsqlCommand command,
        Func<NpgsqlDataReader, T> mapper,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? mapper(reader) : default;
    }

    private static async Task<TelemetryStorePage<T>> ReadPageAsync<T>(
        NpgsqlCommand command,
        int pageSize,
        Func<NpgsqlDataReader, T> mapper,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(mapper(reader));
        }

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

    private static void AddNullableGuid(
        NpgsqlCommand command,
        string name,
        Guid? value) => command.Parameters.Add(
        new NpgsqlParameter(name, NpgsqlDbType.Uuid)
        {
            Value = value is null ? DBNull.Value : value.Value,
        });
}
