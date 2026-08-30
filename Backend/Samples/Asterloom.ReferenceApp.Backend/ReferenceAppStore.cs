using System.Text.Json;
using Npgsql;

namespace Asterloom.ReferenceApp.Backend;

internal sealed record StoredHeartbeat(
    Guid Id,
    string ClientInstanceId,
    string ClientVersion,
    string Platform,
    IReadOnlyDictionary<string, string> Attributes,
    DateTimeOffset RecordedAt);

internal sealed record StoredReferenceStatus(long HeartbeatCount, DateTimeOffset? LastHeartbeatAt);

internal sealed class ReferenceAppStore(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE SCHEMA IF NOT EXISTS reference_app;

            CREATE TABLE IF NOT EXISTS reference_app.client_heartbeats (
                id uuid PRIMARY KEY,
                client_instance_id text NOT NULL,
                client_version text NOT NULL,
                platform text NOT NULL,
                attributes_json jsonb NOT NULL,
                recorded_at timestamptz NOT NULL
            );

            CREATE INDEX IF NOT EXISTS reference_app_client_heartbeats_recorded_idx
                ON reference_app.client_heartbeats (recorded_at DESC);
            """;
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredHeartbeat> RecordAsync(
        string clientInstanceId,
        string clientVersion,
        string platform,
        IReadOnlyDictionary<string, string> attributes,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO reference_app.client_heartbeats (
                id, client_instance_id, client_version, platform, attributes_json, recorded_at)
            VALUES (
                @id, @client_instance_id, @client_version, @platform, @attributes_json::jsonb,
                @recorded_at)
            RETURNING id, client_instance_id, client_version, platform, attributes_json::text,
                recorded_at;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("client_instance_id", clientInstanceId);
        command.Parameters.AddWithValue("client_version", clientVersion);
        command.Parameters.AddWithValue("platform", platform);
        command.Parameters.AddWithValue("attributes_json", JsonSerializer.Serialize(attributes, JsonOptions));
        command.Parameters.AddWithValue("recorded_at", DateTimeOffset.UtcNow);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadHeartbeat(reader);
    }

    public async Task<IReadOnlyList<StoredHeartbeat>> ListAsync(
        int pageSize,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, client_instance_id, client_version, platform, attributes_json::text,
                recorded_at
            FROM reference_app.client_heartbeats
            ORDER BY recorded_at DESC
            LIMIT @page_size;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("page_size", pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<StoredHeartbeat>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadHeartbeat(reader));
        }

        return items;
    }

    public async Task<StoredReferenceStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*), MAX(recorded_at)
            FROM reference_app.client_heartbeats;
            """;
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new StoredReferenceStatus(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
    }

    private static StoredHeartbeat ReadHeartbeat(NpgsqlDataReader reader)
    {
        var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetString(4),
            JsonOptions) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return new StoredHeartbeat(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            attributes,
            reader.GetFieldValue<DateTimeOffset>(5));
    }
}
