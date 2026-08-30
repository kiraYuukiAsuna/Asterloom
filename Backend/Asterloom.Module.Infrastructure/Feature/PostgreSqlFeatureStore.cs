using System.Text.Json;
using Asterloom.Modules.Feature.Model;
using Asterloom.Modules.Feature.Persistence;
using Asterloom.Modules.Outbox;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Feature;

internal sealed class PostgreSqlFeatureStore(
    NpgsqlDataSource dataSource,
    IOutboxStore outboxStore) : IFeatureStore
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private const string FlagColumns =
        """
        id, tenant_id, application_id, environment_id, key, display_name,
        description, value_kind, status, draft_definition::text, draft_revision,
        published_definition::text, published_revision, version, created_at,
        updated_at, archived_at, published_at
        """;

    public async Task<FeatureStorePage<FeatureFlag>> ListFlagsAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        FeaturePageRequest page,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{FlagColumns}}
            FROM feature.flags
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
        var items = new List<FeatureFlag>(page.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadFlag(reader));
        }

        var hasMore = items.Count > page.PageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new FeatureStorePage<FeatureFlag>(items, hasMore);
    }

    public Task<FeatureFlag?> GetFlagAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        Guid flagId,
        CancellationToken cancellationToken) =>
        GetFlagCoreAsync(
            tenantId,
            applicationId,
            environmentId,
            "id = @lookup",
            flagId,
            cancellationToken);

    public Task<FeatureFlag?> GetFlagByKeyAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        string key,
        CancellationToken cancellationToken) =>
        GetFlagCoreAsync(
            tenantId,
            applicationId,
            environmentId,
            "key = @lookup",
            key,
            cancellationToken);

    public async Task<bool> TryCreateFlagAsync(
        FeatureFlag flag,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO feature.flags (
                id, tenant_id, application_id, environment_id, key, display_name,
                description, value_kind, status, draft_definition, draft_revision,
                published_definition, published_revision, version, created_at,
                updated_at, archived_at, published_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @key, @display_name,
                @description, @value_kind, @status, @draft_definition, @draft_revision,
                @published_definition, @published_revision, @version, @created_at,
                @updated_at, @archived_at, @published_at)
            ON CONFLICT DO NOTHING;
            """);
        AddFlagParameters(command, flag);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateFlagAsync(
        FeatureFlag flag,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(CreateUpdateSql());
        AddFlagParameters(command, flag);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<FeatureStorePage<FeatureRevision>> ListRevisionsAsync(
        Guid flagId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, flag_id, tenant_id, application_id, environment_id,
                   revision, definition::text, source_revision, published_at
            FROM feature.revisions
            WHERE flag_id = @flag_id
            ORDER BY revision DESC
            OFFSET @offset
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("flag_id", flagId);
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("limit", pageSize + 1);
        var items = new List<FeatureRevision>(pageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadRevision(reader));
        }

        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new FeatureStorePage<FeatureRevision>(items, hasMore);
    }

    public async Task<FeatureRevision?> GetRevisionAsync(
        Guid flagId,
        long revision,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, flag_id, tenant_id, application_id, environment_id,
                   revision, definition::text, source_revision, published_at
            FROM feature.revisions
            WHERE flag_id = @flag_id AND revision = @revision;
            """);
        command.Parameters.AddWithValue("flag_id", flagId);
        command.Parameters.AddWithValue("revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    public async Task<bool> TryPublishAsync(
        FeatureFlag flag,
        long expectedVersion,
        FeatureRevision revision,
        OutboxMessageDraft integrationEvent,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = CreateUpdateSql();
            AddFlagParameters(update, flag);
            update.Parameters.AddWithValue("expected_version", expectedVersion);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO feature.revisions (
                    id, flag_id, tenant_id, application_id, environment_id,
                    revision, definition, source_revision, published_at)
                VALUES (
                    @id, @flag_id, @tenant_id, @application_id, @environment_id,
                    @revision, @definition, @source_revision, @published_at);
                """;
            AddRevisionParameters(insert, revision);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await outboxStore.EnqueueAsync(
            integrationEvent,
            connection,
            transaction,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<FeatureFlag?> GetFlagCoreAsync<T>(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        string predicate,
        T lookup,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{FlagColumns}}
            FROM feature.flags
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND {{predicate}};
            """);
        AddScopeParameters(command, tenantId, applicationId, environmentId);
        command.Parameters.AddWithValue("lookup", lookup!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadFlag(reader) : null;
    }

    private static string CreateUpdateSql() =>
        """
        UPDATE feature.flags
        SET display_name = @display_name,
            description = @description,
            status = @status,
            draft_definition = @draft_definition,
            draft_revision = @draft_revision,
            published_definition = @published_definition,
            published_revision = @published_revision,
            version = @version,
            updated_at = @updated_at,
            archived_at = @archived_at,
            published_at = @published_at
        WHERE id = @id
          AND tenant_id = @tenant_id
          AND application_id = @application_id
          AND environment_id = @environment_id
          AND key = @key
          AND version = @expected_version;
        """;

    private static void AddFlagParameters(NpgsqlCommand command, FeatureFlag flag)
    {
        command.Parameters.AddWithValue("id", flag.Id);
        AddScopeParameters(command, flag.TenantId, flag.ApplicationId, flag.EnvironmentId);
        command.Parameters.AddWithValue("key", flag.Key);
        command.Parameters.AddWithValue("display_name", flag.DisplayName);
        command.Parameters.AddWithValue("description", flag.Description);
        command.Parameters.AddWithValue("value_kind", (short)flag.ValueKind);
        command.Parameters.AddWithValue("status", (short)flag.Status);
        AddJson(command, "draft_definition", flag.DraftDefinition);
        command.Parameters.AddWithValue("draft_revision", flag.DraftRevision);
        AddNullableJson(command, "published_definition", flag.PublishedDefinition);
        AddNullableInt64(command, "published_revision", flag.PublishedRevision);
        command.Parameters.AddWithValue("version", flag.Version);
        command.Parameters.AddWithValue("created_at", flag.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", flag.UpdatedAt.UtcDateTime);
        AddTimestamp(command, "archived_at", flag.ArchivedAt);
        AddTimestamp(command, "published_at", flag.PublishedAt);
    }

    private static void AddRevisionParameters(
        NpgsqlCommand command,
        FeatureRevision revision)
    {
        command.Parameters.AddWithValue("id", revision.Id);
        command.Parameters.AddWithValue("flag_id", revision.FlagId);
        AddScopeParameters(
            command,
            revision.TenantId,
            revision.ApplicationId,
            revision.EnvironmentId);
        command.Parameters.AddWithValue("revision", revision.Revision);
        AddJson(command, "definition", revision.Definition);
        AddNullableInt64(command, "source_revision", revision.SourceRevision);
        command.Parameters.AddWithValue("published_at", revision.PublishedAt.UtcDateTime);
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

    private static void AddJson<T>(NpgsqlCommand command, string name, T value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(value, SerializerOptions),
            });

    private static void AddNullableJson<T>(NpgsqlCommand command, string name, T? value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
            {
                Value = value is null
                    ? DBNull.Value
                    : JsonSerializer.Serialize(value, SerializerOptions),
            });

    private static void AddNullableInt64(NpgsqlCommand command, string name, long? value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.Bigint)
            {
                Value = value is null ? DBNull.Value : value.Value,
            });

    private static void AddTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset? value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
            {
                Value = value is null ? DBNull.Value : value.Value.UtcDateTime,
            });

    private static FeatureFlag ReadFlag(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            (FeatureValueKind)reader.GetInt16(7),
            (FeatureResourceStatus)reader.GetInt16(8),
            DeserializeDefinition(reader.GetString(9)),
            reader.GetInt64(10),
            reader.IsDBNull(11) ? null : DeserializeDefinition(reader.GetString(11)),
            reader.IsDBNull(12) ? null : reader.GetInt64(12),
            reader.GetInt64(13),
            ToDateTimeOffset(reader.GetDateTime(14)),
            ToDateTimeOffset(reader.GetDateTime(15)),
            reader.IsDBNull(16) ? null : ToDateTimeOffset(reader.GetDateTime(16)),
            reader.IsDBNull(17) ? null : ToDateTimeOffset(reader.GetDateTime(17)));

    private static FeatureRevision ReadRevision(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetInt64(5),
            DeserializeDefinition(reader.GetString(6)),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            ToDateTimeOffset(reader.GetDateTime(8)));

    private static FeatureDefinition DeserializeDefinition(string json) =>
        JsonSerializer.Deserialize<FeatureDefinition>(json, SerializerOptions)
        ?? throw new InvalidOperationException("A feature definition is empty.");

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
