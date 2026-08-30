using System.Text.Json;
using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Config.Persistence;
using Asterloom.Modules.Outbox;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Config;

internal sealed class PostgreSqlConfigStore(
    NpgsqlDataSource dataSource,
    IOutboxStore outboxStore) : IConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private const string EntryColumns =
        """
        id, tenant_id, application_id, environment_id, key, display_name,
        description, value_kind, visibility, status, draft_definition::text,
        draft_revision, published_definition::text, published_revision,
        published_snapshot_version, version, created_at, updated_at,
        archived_at, published_at
        """;

    public async Task<ConfigStorePage<ConfigEntry>> ListEntriesAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        ConfigPageRequest page,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{EntryColumns}}
            FROM config.entries
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
        var items = new List<ConfigEntry>(page.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadEntry(reader));
        }

        var hasMore = items.Count > page.PageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }
        return new(items, hasMore);
    }

    public Task<ConfigEntry?> GetEntryAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        Guid entryId,
        CancellationToken cancellationToken) =>
        GetEntryCoreAsync(
            tenantId,
            applicationId,
            environmentId,
            "id = @lookup",
            entryId,
            cancellationToken);

    public Task<ConfigEntry?> GetEntryByKeyAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        string key,
        CancellationToken cancellationToken) =>
        GetEntryCoreAsync(
            tenantId,
            applicationId,
            environmentId,
            "key = @lookup",
            key,
            cancellationToken);

    public async Task<IReadOnlyList<ConfigEntry>> ListPublishedEntriesAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{EntryColumns}}
            FROM config.entries
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND published_definition IS NOT NULL
            ORDER BY key;
            """);
        AddScopeParameters(command, tenantId, applicationId, environmentId);
        var items = new List<ConfigEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadEntry(reader));
        }
        return items;
    }

    public async Task<bool> TryCreateEntryAsync(
        ConfigEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO config.entries (
                id, tenant_id, application_id, environment_id, key, display_name,
                description, value_kind, visibility, status, draft_definition,
                draft_revision, published_definition, published_revision,
                published_snapshot_version, version, created_at, updated_at,
                archived_at, published_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @key, @display_name,
                @description, @value_kind, @visibility, @status, @draft_definition,
                @draft_revision, @published_definition, @published_revision,
                @published_snapshot_version, @version, @created_at, @updated_at,
                @archived_at, @published_at)
            ON CONFLICT DO NOTHING;
            """);
        AddEntryParameters(command, entry);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateEntryAsync(
        ConfigEntry entry,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(CreateUpdateSql());
        AddEntryParameters(command, entry);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<ConfigStorePage<ConfigRevision>> ListRevisionsAsync(
        Guid entryId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, entry_id, tenant_id, application_id, environment_id,
                   revision, definition::text, source_revision, snapshot_version,
                   published_at
            FROM config.revisions
            WHERE entry_id = @entry_id
            ORDER BY revision DESC
            OFFSET @offset
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("entry_id", entryId);
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("limit", pageSize + 1);
        var items = new List<ConfigRevision>(pageSize + 1);
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
        return new(items, hasMore);
    }

    public async Task<ConfigRevision?> GetRevisionAsync(
        Guid entryId,
        long revision,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, entry_id, tenant_id, application_id, environment_id,
                   revision, definition::text, source_revision, snapshot_version,
                   published_at
            FROM config.revisions
            WHERE entry_id = @entry_id AND revision = @revision;
            """);
        command.Parameters.AddWithValue("entry_id", entryId);
        command.Parameters.AddWithValue("revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    public async Task<ConfigSnapshot?> GetLatestSnapshotAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, tenant_id, application_id, environment_id, version,
                   items::text, created_at
            FROM config.snapshots
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
            ORDER BY version DESC
            LIMIT 1;
            """);
        AddScopeParameters(command, tenantId, applicationId, environmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSnapshot(reader) : null;
    }

    public async Task<ConfigStorePage<ConfigSnapshot>> ListSnapshotsAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, tenant_id, application_id, environment_id, version,
                   items::text, created_at
            FROM config.snapshots
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
            ORDER BY version DESC
            OFFSET @offset
            LIMIT @limit;
            """);
        AddScopeParameters(command, tenantId, applicationId, environmentId);
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("limit", pageSize + 1);
        var items = new List<ConfigSnapshot>(pageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadSnapshot(reader));
        }
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }
        return new(items, hasMore);
    }

    public async Task<bool> TryCommitSnapshotAsync(
        ConfigEntry entry,
        long expectedVersion,
        ConfigRevision? revision,
        ConfigSnapshot snapshot,
        OutboxMessageDraft integrationEvent,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireScopeLockAsync(connection, transaction, snapshot, cancellationToken);
        var latestVersion = await GetLatestVersionAsync(
            connection,
            transaction,
            snapshot,
            cancellationToken);
        if (snapshot.Version != latestVersion + 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = CreateUpdateSql();
            AddEntryParameters(update, entry);
            update.Parameters.AddWithValue("expected_version", expectedVersion);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        if (revision is not null)
        {
            await using var insertRevision = connection.CreateCommand();
            insertRevision.Transaction = transaction;
            insertRevision.CommandText =
                """
                INSERT INTO config.revisions (
                    id, entry_id, tenant_id, application_id, environment_id,
                    revision, definition, source_revision, snapshot_version, published_at)
                VALUES (
                    @id, @entry_id, @tenant_id, @application_id, @environment_id,
                    @revision, @definition, @source_revision, @snapshot_version, @published_at);
                """;
            AddRevisionParameters(insertRevision, revision);
            await insertRevision.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertSnapshot = connection.CreateCommand())
        {
            insertSnapshot.Transaction = transaction;
            insertSnapshot.CommandText =
                """
                INSERT INTO config.snapshots (
                    id, tenant_id, application_id, environment_id, version, items, created_at)
                VALUES (
                    @id, @tenant_id, @application_id, @environment_id,
                    @version, @items, @created_at);
                """;
            AddSnapshotParameters(insertSnapshot, snapshot);
            await insertSnapshot.ExecuteNonQueryAsync(cancellationToken);
        }

        await outboxStore.EnqueueAsync(
            integrationEvent,
            connection,
            transaction,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<ConfigEntry?> GetEntryCoreAsync<T>(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        string predicate,
        T lookup,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{EntryColumns}}
            FROM config.entries
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND {{predicate}};
            """);
        AddScopeParameters(command, tenantId, applicationId, environmentId);
        command.Parameters.AddWithValue("lookup", lookup!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntry(reader) : null;
    }

    private static async Task AcquireScopeLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ConfigSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT pg_advisory_xact_lock(hashtextextended(@scope_key, 0));
            """;
        command.Parameters.AddWithValue(
            "scope_key",
            $"config:{snapshot.TenantId:D}:{snapshot.ApplicationId:D}:{snapshot.EnvironmentId:D}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> GetLatestVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ConfigSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COALESCE(MAX(version), 0)
            FROM config.snapshots
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id;
            """;
        AddScopeParameters(
            command,
            snapshot.TenantId,
            snapshot.ApplicationId,
            snapshot.EnvironmentId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateUpdateSql() =>
        """
        UPDATE config.entries
        SET display_name = @display_name,
            description = @description,
            visibility = @visibility,
            status = @status,
            draft_definition = @draft_definition,
            draft_revision = @draft_revision,
            published_definition = @published_definition,
            published_revision = @published_revision,
            published_snapshot_version = @published_snapshot_version,
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

    private static void AddEntryParameters(NpgsqlCommand command, ConfigEntry entry)
    {
        command.Parameters.AddWithValue("id", entry.Id);
        AddScopeParameters(command, entry.TenantId, entry.ApplicationId, entry.EnvironmentId);
        command.Parameters.AddWithValue("key", entry.Key);
        command.Parameters.AddWithValue("display_name", entry.DisplayName);
        command.Parameters.AddWithValue("description", entry.Description);
        command.Parameters.AddWithValue("value_kind", (short)entry.ValueKind);
        command.Parameters.AddWithValue("visibility", (short)entry.Visibility);
        command.Parameters.AddWithValue("status", (short)entry.Status);
        AddJson(command, "draft_definition", entry.DraftDefinition);
        command.Parameters.AddWithValue("draft_revision", entry.DraftRevision);
        AddNullableJson(command, "published_definition", entry.PublishedDefinition);
        AddNullableInt64(command, "published_revision", entry.PublishedRevision);
        AddNullableInt64(command, "published_snapshot_version", entry.PublishedSnapshotVersion);
        command.Parameters.AddWithValue("version", entry.Version);
        command.Parameters.AddWithValue("created_at", entry.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", entry.UpdatedAt.UtcDateTime);
        AddTimestamp(command, "archived_at", entry.ArchivedAt);
        AddTimestamp(command, "published_at", entry.PublishedAt);
    }

    private static void AddRevisionParameters(
        NpgsqlCommand command,
        ConfigRevision revision)
    {
        command.Parameters.AddWithValue("id", revision.Id);
        command.Parameters.AddWithValue("entry_id", revision.EntryId);
        AddScopeParameters(
            command,
            revision.TenantId,
            revision.ApplicationId,
            revision.EnvironmentId);
        command.Parameters.AddWithValue("revision", revision.Revision);
        AddJson(command, "definition", revision.Definition);
        AddNullableInt64(command, "source_revision", revision.SourceRevision);
        command.Parameters.AddWithValue("snapshot_version", revision.SnapshotVersion);
        command.Parameters.AddWithValue("published_at", revision.PublishedAt.UtcDateTime);
    }

    private static void AddSnapshotParameters(
        NpgsqlCommand command,
        ConfigSnapshot snapshot)
    {
        command.Parameters.AddWithValue("id", snapshot.Id);
        AddScopeParameters(
            command,
            snapshot.TenantId,
            snapshot.ApplicationId,
            snapshot.EnvironmentId);
        command.Parameters.AddWithValue("version", snapshot.Version);
        AddJson(command, "items", snapshot.Items);
        command.Parameters.AddWithValue("created_at", snapshot.CreatedAt.UtcDateTime);
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
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value, SerializerOptions),
        });

    private static void AddNullableJson<T>(NpgsqlCommand command, string name, T? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = value is null
                ? DBNull.Value
                : JsonSerializer.Serialize(value, SerializerOptions),
        });

    private static void AddNullableInt64(NpgsqlCommand command, string name, long? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Bigint)
        {
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static void AddTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
        {
            Value = value is null ? DBNull.Value : value.Value.UtcDateTime,
        });

    private static ConfigEntry ReadEntry(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            (ConfigValueKind)reader.GetInt16(7),
            (ConfigVisibility)reader.GetInt16(8),
            (ConfigResourceStatus)reader.GetInt16(9),
            DeserializeDefinition(reader.GetString(10)),
            reader.GetInt64(11),
            reader.IsDBNull(12) ? null : DeserializeDefinition(reader.GetString(12)),
            reader.IsDBNull(13) ? null : reader.GetInt64(13),
            reader.IsDBNull(14) ? null : reader.GetInt64(14),
            reader.GetInt64(15),
            ToDateTimeOffset(reader.GetDateTime(16)),
            ToDateTimeOffset(reader.GetDateTime(17)),
            reader.IsDBNull(18) ? null : ToDateTimeOffset(reader.GetDateTime(18)),
            reader.IsDBNull(19) ? null : ToDateTimeOffset(reader.GetDateTime(19)));

    private static ConfigRevision ReadRevision(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetInt64(5),
            DeserializeDefinition(reader.GetString(6)),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.GetInt64(8),
            ToDateTimeOffset(reader.GetDateTime(9)));

    private static ConfigSnapshot ReadSnapshot(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetInt64(4),
            JsonSerializer.Deserialize<ConfigSnapshotItem[]>(
                reader.GetString(5),
                SerializerOptions)
                ?? throw new InvalidOperationException("A configuration snapshot is empty."),
            ToDateTimeOffset(reader.GetDateTime(6)));

    private static ConfigDefinition DeserializeDefinition(string json) =>
        JsonSerializer.Deserialize<ConfigDefinition>(json, SerializerOptions)
        ?? throw new InvalidOperationException("A configuration definition is empty.");

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
