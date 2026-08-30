using System.Text.Json;
using Asterloom.Modules.Storage.Model;
using Asterloom.Modules.Storage.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Storage;

internal sealed class PostgreSqlStorageStore(NpgsqlDataSource dataSource) : IStorageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string BucketColumns =
        """
        id, tenant_id, key, display_name, description, quota_bytes,
        max_object_size_bytes, allowed_content_types::text, access_policy,
        status, used_bytes, reserved_bytes, object_count, version, created_at,
        updated_at, archived_at
        """;

    private const string ObjectColumns =
        """
        id, tenant_id, bucket_id, application_id, environment_id, object_key,
        physical_key, file_name, content_type, size_bytes, sha256,
        custom_metadata::text, status, version, created_at, updated_at,
        completed_at, deleted_at
        """;

    private const string SessionColumns =
        """
        id, tenant_id, bucket_id, object_id, transfer::text, status,
        failure_reason, version, created_at, expires_at, completed_at
        """;

    public async Task<StorageStorePage<StorageBucket>> ListBucketsAsync(
        Guid tenantId,
        StoragePageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{BucketColumns}}
            FROM storage.buckets
            WHERE tenant_id = @tenant_id
              AND (@include_inactive OR status = 1)
              AND (@query = '' OR key ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%'
                   OR description ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset LIMIT @limit;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddPageParameters(command, request);
        return await ReadPageAsync(command, request.Limit, ReadBucket, cancellationToken);
    }

    public Task<StorageBucket?> GetBucketAsync(
        Guid tenantId,
        Guid bucketId,
        CancellationToken cancellationToken) =>
        GetBucketCoreAsync(tenantId, "id = @lookup", bucketId, cancellationToken);

    public Task<StorageBucket?> GetBucketByKeyAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken) =>
        GetBucketCoreAsync(tenantId, "key = @lookup", key, cancellationToken);

    public async Task<bool> TryCreateBucketAsync(
        StorageBucket bucket,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(CreateBucketInsertSql());
        AddBucketParameters(command, bucket);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateBucketAsync(
        StorageBucket bucket,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(CreateBucketUpdateSql());
        AddBucketParameters(command, bucket);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<StorageStorePage<StorageObject>> ListObjectsAsync(
        Guid tenantId,
        Guid bucketId,
        StoragePageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{ObjectColumns}}
            FROM storage.objects
            WHERE tenant_id = @tenant_id AND bucket_id = @bucket_id
              AND (@include_inactive OR status <> 4)
              AND (@query = '' OR object_key ILIKE '%' || @query || '%'
                   OR file_name ILIKE '%' || @query || '%')
            ORDER BY created_at DESC, id
            OFFSET @offset LIMIT @limit;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("bucket_id", bucketId);
        AddPageParameters(command, request);
        return await ReadPageAsync(command, request.Limit, ReadObject, cancellationToken);
    }

    public Task<StorageObject?> GetObjectAsync(
        Guid tenantId,
        Guid bucketId,
        Guid objectId,
        CancellationToken cancellationToken) =>
        GetObjectCoreAsync(tenantId, bucketId, "id = @lookup", objectId, cancellationToken);

    public Task<StorageObject?> GetObjectByKeyAsync(
        Guid tenantId,
        Guid bucketId,
        string objectKey,
        CancellationToken cancellationToken) =>
        GetObjectCoreAsync(tenantId, bucketId, "object_key = @lookup", objectKey, cancellationToken);

    public async Task<StorageUploadSession?> GetUploadSessionAsync(
        Guid tenantId,
        Guid bucketId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{SessionColumns}}
            FROM storage.upload_sessions
            WHERE tenant_id = @tenant_id AND bucket_id = @bucket_id AND id = @id;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("bucket_id", bucketId);
        command.Parameters.AddWithValue("id", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    public async Task<bool> TryCreateUploadAsync(
        StorageBucket reservedBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        StorageUploadSession session,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await UpdateBucketAsync(connection, transaction, reservedBucket, expectedBucketVersion, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await using (var objectCommand = connection.CreateCommand())
        {
            objectCommand.Transaction = transaction;
            objectCommand.CommandText = CreateObjectInsertSql();
            AddObjectParameters(objectCommand, storageObject);
            if (await objectCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }
        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.Transaction = transaction;
            sessionCommand.CommandText = CreateSessionInsertSql();
            AddSessionParameters(sessionCommand, session);
            if (await sessionCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<bool> TryCompleteUploadAsync(
        StorageBucket completedBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        StorageUploadSession session,
        long expectedSessionVersion,
        CancellationToken cancellationToken) =>
        TryUpdateUploadAsync(
            completedBucket,
            expectedBucketVersion,
            storageObject,
            expectedObjectVersion,
            session,
            expectedSessionVersion,
            cancellationToken);

    public Task<bool> TryFailUploadAsync(
        StorageBucket releasedBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        StorageUploadSession session,
        long expectedSessionVersion,
        CancellationToken cancellationToken) =>
        TryUpdateUploadAsync(
            releasedBucket,
            expectedBucketVersion,
            storageObject,
            expectedObjectVersion,
            session,
            expectedSessionVersion,
            cancellationToken);

    public async Task<bool> TryUpdateObjectAsync(
        StorageObject storageObject,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(CreateObjectUpdateSql());
        AddObjectParameters(command, storageObject);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryCopyObjectAsync(
        StorageBucket targetBucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await UpdateBucketAsync(connection, transaction, targetBucket, expectedBucketVersion, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CreateObjectInsertSql();
        AddObjectParameters(command, storageObject);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryDeleteObjectAsync(
        StorageBucket bucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await UpdateBucketAsync(connection, transaction, bucket, expectedBucketVersion, cancellationToken)
            || !await UpdateObjectAsync(connection, transaction, storageObject, expectedObjectVersion, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryUpdateUploadAsync(
        StorageBucket bucket,
        long expectedBucketVersion,
        StorageObject storageObject,
        long expectedObjectVersion,
        StorageUploadSession session,
        long expectedSessionVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await UpdateBucketAsync(connection, transaction, bucket, expectedBucketVersion, cancellationToken)
            || !await UpdateObjectAsync(connection, transaction, storageObject, expectedObjectVersion, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CreateSessionUpdateSql();
        AddSessionParameters(command, session);
        command.Parameters.AddWithValue("expected_version", expectedSessionVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<StorageBucket?> GetBucketCoreAsync<T>(
        Guid tenantId,
        string predicate,
        T lookup,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{BucketColumns}} FROM storage.buckets
            WHERE tenant_id = @tenant_id AND {{predicate}};
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("lookup", lookup!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBucket(reader) : null;
    }

    private async Task<StorageObject?> GetObjectCoreAsync<T>(
        Guid tenantId,
        Guid bucketId,
        string predicate,
        T lookup,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{ObjectColumns}} FROM storage.objects
            WHERE tenant_id = @tenant_id AND bucket_id = @bucket_id AND {{predicate}};
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("bucket_id", bucketId);
        command.Parameters.AddWithValue("lookup", lookup!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadObject(reader) : null;
    }

    private static async Task<bool> UpdateBucketAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StorageBucket bucket,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CreateBucketUpdateSql();
        AddBucketParameters(command, bucket);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<bool> UpdateObjectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StorageObject storageObject,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CreateObjectUpdateSql();
        AddObjectParameters(command, storageObject);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static string CreateBucketInsertSql() =>
        """
        INSERT INTO storage.buckets (
            id, tenant_id, key, display_name, description, quota_bytes,
            max_object_size_bytes, allowed_content_types, access_policy, status,
            used_bytes, reserved_bytes, object_count, version, created_at,
            updated_at, archived_at)
        VALUES (
            @id, @tenant_id, @key, @display_name, @description, @quota_bytes,
            @max_object_size_bytes, @allowed_content_types, @access_policy, @status,
            @used_bytes, @reserved_bytes, @object_count, @version, @created_at,
            @updated_at, @archived_at)
        ON CONFLICT DO NOTHING;
        """;

    private static string CreateBucketUpdateSql() =>
        """
        UPDATE storage.buckets SET
            display_name = @display_name, description = @description,
            quota_bytes = @quota_bytes, max_object_size_bytes = @max_object_size_bytes,
            allowed_content_types = @allowed_content_types, access_policy = @access_policy,
            status = @status, used_bytes = @used_bytes, reserved_bytes = @reserved_bytes,
            object_count = @object_count, version = @version, updated_at = @updated_at,
            archived_at = @archived_at
        WHERE id = @id AND tenant_id = @tenant_id AND key = @key
          AND version = @expected_version;
        """;

    private static string CreateObjectInsertSql() =>
        """
        INSERT INTO storage.objects (
            id, tenant_id, bucket_id, application_id, environment_id, object_key,
            physical_key, file_name, content_type, size_bytes, sha256,
            custom_metadata, status, version, created_at, updated_at,
            completed_at, deleted_at)
        VALUES (
            @id, @tenant_id, @bucket_id, @application_id, @environment_id, @object_key,
            @physical_key, @file_name, @content_type, @size_bytes, @sha256,
            @custom_metadata, @status, @version, @created_at, @updated_at,
            @completed_at, @deleted_at)
        ON CONFLICT DO NOTHING;
        """;

    private static string CreateObjectUpdateSql() =>
        """
        UPDATE storage.objects SET
            file_name = @file_name, content_type = @content_type, size_bytes = @size_bytes,
            sha256 = @sha256, custom_metadata = @custom_metadata, status = @status,
            version = @version, updated_at = @updated_at, completed_at = @completed_at,
            deleted_at = @deleted_at
        WHERE id = @id AND tenant_id = @tenant_id AND bucket_id = @bucket_id
          AND object_key = @object_key AND version = @expected_version;
        """;

    private static string CreateSessionInsertSql() =>
        """
        INSERT INTO storage.upload_sessions (
            id, tenant_id, bucket_id, object_id, transfer, status, failure_reason,
            version, created_at, expires_at, completed_at)
        VALUES (
            @id, @tenant_id, @bucket_id, @object_id, @transfer, @status, @failure_reason,
            @version, @created_at, @expires_at, @completed_at)
        ON CONFLICT DO NOTHING;
        """;

    private static string CreateSessionUpdateSql() =>
        """
        UPDATE storage.upload_sessions SET
            transfer = @transfer, status = @status, failure_reason = @failure_reason,
            version = @version, expires_at = @expires_at, completed_at = @completed_at
        WHERE id = @id AND tenant_id = @tenant_id AND bucket_id = @bucket_id
          AND object_id = @object_id AND version = @expected_version;
        """;

    private static void AddBucketParameters(NpgsqlCommand command, StorageBucket bucket)
    {
        command.Parameters.AddWithValue("id", bucket.Id);
        command.Parameters.AddWithValue("tenant_id", bucket.TenantId);
        command.Parameters.AddWithValue("key", bucket.Key);
        command.Parameters.AddWithValue("display_name", bucket.DisplayName);
        command.Parameters.AddWithValue("description", bucket.Description);
        command.Parameters.AddWithValue("quota_bytes", bucket.QuotaBytes);
        command.Parameters.AddWithValue("max_object_size_bytes", bucket.MaxObjectSizeBytes);
        AddJson(command, "allowed_content_types", bucket.AllowedContentTypes);
        command.Parameters.AddWithValue("access_policy", (short)bucket.AccessPolicy);
        command.Parameters.AddWithValue("status", (short)bucket.Status);
        command.Parameters.AddWithValue("used_bytes", bucket.UsedBytes);
        command.Parameters.AddWithValue("reserved_bytes", bucket.ReservedBytes);
        command.Parameters.AddWithValue("object_count", bucket.ObjectCount);
        command.Parameters.AddWithValue("version", bucket.Version);
        command.Parameters.AddWithValue("created_at", bucket.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", bucket.UpdatedAt.UtcDateTime);
        AddTimestamp(command, "archived_at", bucket.ArchivedAt);
    }

    private static void AddObjectParameters(NpgsqlCommand command, StorageObject storageObject)
    {
        command.Parameters.AddWithValue("id", storageObject.Id);
        command.Parameters.AddWithValue("tenant_id", storageObject.TenantId);
        command.Parameters.AddWithValue("bucket_id", storageObject.BucketId);
        AddGuid(command, "application_id", storageObject.ApplicationId);
        AddGuid(command, "environment_id", storageObject.EnvironmentId);
        command.Parameters.AddWithValue("object_key", storageObject.ObjectKey);
        command.Parameters.AddWithValue("physical_key", storageObject.PhysicalKey);
        command.Parameters.AddWithValue("file_name", storageObject.FileName);
        command.Parameters.AddWithValue("content_type", storageObject.ContentType);
        command.Parameters.AddWithValue("size_bytes", storageObject.SizeBytes);
        command.Parameters.AddWithValue("sha256", storageObject.Sha256);
        AddJson(command, "custom_metadata", storageObject.CustomMetadata);
        command.Parameters.AddWithValue("status", (short)storageObject.Status);
        command.Parameters.AddWithValue("version", storageObject.Version);
        command.Parameters.AddWithValue("created_at", storageObject.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", storageObject.UpdatedAt.UtcDateTime);
        AddTimestamp(command, "completed_at", storageObject.CompletedAt);
        AddTimestamp(command, "deleted_at", storageObject.DeletedAt);
    }

    private static void AddSessionParameters(NpgsqlCommand command, StorageUploadSession session)
    {
        command.Parameters.AddWithValue("id", session.Id);
        command.Parameters.AddWithValue("tenant_id", session.TenantId);
        command.Parameters.AddWithValue("bucket_id", session.BucketId);
        command.Parameters.AddWithValue("object_id", session.ObjectId);
        AddJson(command, "transfer", session.Transfer);
        command.Parameters.AddWithValue("status", (short)session.Status);
        command.Parameters.AddWithValue("failure_reason", session.FailureReason);
        command.Parameters.AddWithValue("version", session.Version);
        command.Parameters.AddWithValue("created_at", session.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("expires_at", session.ExpiresAt.UtcDateTime);
        AddTimestamp(command, "completed_at", session.CompletedAt);
    }

    private static void AddPageParameters(NpgsqlCommand command, StoragePageRequest request)
    {
        command.Parameters.AddWithValue("include_inactive", request.IncludeInactive);
        command.Parameters.AddWithValue("query", request.Query);
        command.Parameters.AddWithValue("offset", request.Offset);
        command.Parameters.AddWithValue("limit", request.Limit + 1);
    }

    private static void AddJson<T>(NpgsqlCommand command, string name, T value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value, JsonOptions),
        });

    private static void AddGuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid)
        {
            Value = value.HasValue ? value.Value : DBNull.Value,
        });

    private static void AddTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
        {
            Value = value.HasValue ? value.Value.UtcDateTime : DBNull.Value,
        });

    private static async Task<StorageStorePage<T>> ReadPageAsync<T>(
        NpgsqlCommand command,
        int limit,
        Func<NpgsqlDataReader, T> read,
        CancellationToken cancellationToken)
    {
        var items = new List<T>(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(read(reader));
        }
        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }
        return new StorageStorePage<T>(items, hasMore);
    }

    private static StorageBucket ReadBucket(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetInt64(6),
        Deserialize<string[]>(reader.GetString(7)),
        (StorageAccessPolicy)reader.GetInt16(8),
        (StorageResourceStatus)reader.GetInt16(9),
        reader.GetInt64(10),
        reader.GetInt64(11),
        reader.GetInt64(12),
        reader.GetInt64(13),
        ToOffset(reader.GetDateTime(14)),
        ToOffset(reader.GetDateTime(15)),
        reader.IsDBNull(16) ? null : ToOffset(reader.GetDateTime(16)));

    private static StorageObject ReadObject(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetGuid(2),
        reader.IsDBNull(3) ? null : reader.GetGuid(3),
        reader.IsDBNull(4) ? null : reader.GetGuid(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetInt64(9),
        reader.GetString(10),
        Deserialize<Dictionary<string, string>>(reader.GetString(11)),
        (StorageObjectStatus)reader.GetInt16(12),
        reader.GetInt64(13),
        ToOffset(reader.GetDateTime(14)),
        ToOffset(reader.GetDateTime(15)),
        reader.IsDBNull(16) ? null : ToOffset(reader.GetDateTime(16)),
        reader.IsDBNull(17) ? null : ToOffset(reader.GetDateTime(17)));

    private static StorageUploadSession ReadSession(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetGuid(2),
        reader.GetGuid(3),
        Deserialize<StorageTransferTicket>(reader.GetString(4)),
        (StorageUploadStatus)reader.GetInt16(5),
        reader.GetString(6),
        reader.GetInt64(7),
        ToOffset(reader.GetDateTime(8)),
        ToOffset(reader.GetDateTime(9)),
        reader.IsDBNull(10) ? null : ToOffset(reader.GetDateTime(10)));

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException("Stored JSON is empty.");

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
