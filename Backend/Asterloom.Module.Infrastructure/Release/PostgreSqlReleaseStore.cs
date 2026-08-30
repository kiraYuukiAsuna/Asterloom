using System.Text.Json;
using Asterloom.Modules.Release.Model;
using Asterloom.Modules.Release.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Release;

internal sealed class PostgreSqlReleaseStore(NpgsqlDataSource dataSource) : IReleaseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string SigningKeyColumns =
        "id, tenant_id, key, display_name, algorithm, fingerprint, public_key_pem, "
        + "status, version, created_at, updated_at, archived_at";

    private const string ChannelColumns =
        "id, tenant_id, application_id, environment_id, key, display_name, description, "
        + "status, active_release_id, previous_release_id, version, created_at, updated_at, archived_at";

    private const string ArtifactColumns =
        "id, tenant_id, application_id, environment_id, release_version, target_runtime_id, "
        + "artifact_kind, delta_from_version, file_name, content_type, size_bytes, sha256, "
        + "signing_key_id, signature, status, failure_reason, storage_bucket_id, storage_object_id, "
        + "upload_session_id, storage_object_version, version, created_at, updated_at, verified_at, archived_at";

    private const string ReleaseColumns =
        "id, tenant_id, application_id, environment_id, channel_id, release_version, display_name, "
        + "release_notes, artifact_ids::text, rollout_basis_points, target_segment_id, mandatory, "
        + "minimum_version, bucketing_salt, status, revision, manifest_payload_json, manifest_sha256, "
        + "manifest_signature, manifest_signing_key_id, manifest_signing_key_fingerprint, "
        + "manifest_generated_at, version, created_at, updated_at, published_at, paused_at, rolled_back_at";

    public async Task<ReleaseStorePage<ReleaseSigningKey>> ListSigningKeysAsync(
        Guid tenantId,
        ReleasePageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{SigningKeyColumns}}
            FROM release.signing_keys
            WHERE tenant_id = @tenant_id
              AND (@include_inactive OR status = 1)
              AND (@query = '' OR key ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%'
                   OR fingerprint ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset LIMIT @limit;
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddPageParameters(command, request);
        return await ReadPageAsync(command, request.PageSize, ReadSigningKey, cancellationToken);
    }

    public Task<ReleaseSigningKey?> GetSigningKeyAsync(
        Guid tenantId,
        Guid signingKeyId,
        CancellationToken cancellationToken) =>
        GetSigningKeyCoreAsync(tenantId, signingKeyId, cancellationToken);

    public async Task<bool> TryCreateSigningKeyAsync(
        ReleaseSigningKey signingKey,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO release.signing_keys (
                id, tenant_id, key, display_name, algorithm, fingerprint, public_key_pem,
                status, version, created_at, updated_at, archived_at)
            VALUES (
                @id, @tenant_id, @key, @display_name, @algorithm, @fingerprint, @public_key_pem,
                @status, @version, @created_at, @updated_at, @archived_at)
            ON CONFLICT DO NOTHING;
            """);
        AddSigningKeyParameters(command, signingKey);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateSigningKeyAsync(
        ReleaseSigningKey signingKey,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(SigningKeyUpdateSql());
        AddSigningKeyParameters(command, signingKey);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<ReleaseStorePage<ReleaseChannel>> ListChannelsAsync(
        ReleaseScope scope,
        ReleasePageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{ChannelColumns}}
            FROM release.channels
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND (@include_inactive OR status = 1)
              AND (@query = '' OR key ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%'
                   OR description ILIKE '%' || @query || '%')
            ORDER BY lower(display_name), id
            OFFSET @offset LIMIT @limit;
            """);
        AddScopeParameters(command, scope);
        AddPageParameters(command, request);
        return await ReadPageAsync(command, request.PageSize, ReadChannel, cancellationToken);
    }

    public Task<ReleaseChannel?> GetChannelAsync(
        ReleaseScope scope,
        Guid channelId,
        CancellationToken cancellationToken) =>
        GetChannelCoreAsync(scope, "id = @lookup", channelId, cancellationToken);

    public Task<ReleaseChannel?> GetChannelByKeyAsync(
        ReleaseScope scope,
        string key,
        CancellationToken cancellationToken) =>
        GetChannelCoreAsync(scope, "key = @lookup", key, cancellationToken);

    public async Task<bool> TryCreateChannelAsync(
        ReleaseChannel channel,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO release.channels (
                id, tenant_id, application_id, environment_id, key, display_name, description,
                status, active_release_id, previous_release_id, version, created_at, updated_at, archived_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @key, @display_name, @description,
                @status, @active_release_id, @previous_release_id, @version, @created_at, @updated_at, @archived_at)
            ON CONFLICT DO NOTHING;
            """);
        AddChannelParameters(command, channel);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateChannelAsync(
        ReleaseChannel channel,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ChannelUpdateSql());
        AddChannelParameters(command, channel);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<ReleaseStorePage<ReleaseArtifact>> ListArtifactsAsync(
        ReleaseScope scope,
        ReleasePageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{ArtifactColumns}}
            FROM release.artifacts
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND (@include_inactive OR status <> 4)
              AND (@query = '' OR release_version ILIKE '%' || @query || '%'
                   OR target_runtime_id ILIKE '%' || @query || '%'
                   OR file_name ILIKE '%' || @query || '%'
                   OR sha256 ILIKE '%' || @query || '%')
            ORDER BY created_at DESC, id
            OFFSET @offset LIMIT @limit;
            """);
        AddScopeParameters(command, scope);
        AddPageParameters(command, request);
        return await ReadPageAsync(command, request.PageSize, ReadArtifact, cancellationToken);
    }

    public Task<ReleaseArtifact?> GetArtifactAsync(
        ReleaseScope scope,
        Guid artifactId,
        CancellationToken cancellationToken) =>
        GetArtifactCoreAsync(
            scope,
            "id = @lookup",
            artifactId,
            null,
            cancellationToken);

    public Task<ReleaseArtifact?> GetArtifactByIdentityAsync(
        ReleaseScope scope,
        string releaseVersion,
        string targetRuntimeId,
        ReleaseArtifactKind artifactKind,
        string deltaFromVersion,
        CancellationToken cancellationToken) =>
        GetArtifactCoreAsync(
            scope,
            "release_version = @release_version AND target_runtime_id = @target_runtime_id "
            + "AND artifact_kind = @artifact_kind AND delta_from_version = @delta_from_version",
            null,
            command =>
            {
                command.Parameters.AddWithValue("release_version", releaseVersion);
                command.Parameters.AddWithValue("target_runtime_id", targetRuntimeId);
                command.Parameters.AddWithValue("artifact_kind", (short)artifactKind);
                command.Parameters.AddWithValue("delta_from_version", deltaFromVersion);
            },
            cancellationToken);

    public async Task<bool> TryCreateArtifactAsync(
        ReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO release.artifacts (
                id, tenant_id, application_id, environment_id, release_version, target_runtime_id,
                artifact_kind, delta_from_version, file_name, content_type, size_bytes, sha256,
                signing_key_id, signature, status, failure_reason, storage_bucket_id, storage_object_id,
                upload_session_id, storage_object_version, version, created_at, updated_at, verified_at, archived_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @release_version, @target_runtime_id,
                @artifact_kind, @delta_from_version, @file_name, @content_type, @size_bytes, @sha256,
                @signing_key_id, @signature, @status, @failure_reason, @storage_bucket_id, @storage_object_id,
                @upload_session_id, @storage_object_version, @version, @created_at, @updated_at, @verified_at, @archived_at)
            ON CONFLICT DO NOTHING;
            """);
        AddArtifactParameters(command, artifact);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateArtifactAsync(
        ReleaseArtifact artifact,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ArtifactUpdateSql());
        AddArtifactParameters(command, artifact);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> IsArtifactReferencedByLiveReleaseAsync(
        ReleaseScope scope,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM release.releases
                WHERE tenant_id = @tenant_id
                  AND application_id = @application_id
                  AND environment_id = @environment_id
                  AND status <> 4
                  AND artifact_ids ? @artifact_id);
            """);
        AddScopeParameters(command, scope);
        command.Parameters.AddWithValue("artifact_id", artifactId.ToString("D"));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<ReleaseStorePage<DesktopRelease>> ListReleasesAsync(
        ReleaseScope scope,
        ReleasePageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{ReleaseColumns}}
            FROM release.releases
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND environment_id = @environment_id
              AND (@include_inactive OR status <> 4)
              AND (@query = '' OR release_version ILIKE '%' || @query || '%'
                   OR display_name ILIKE '%' || @query || '%'
                   OR release_notes ILIKE '%' || @query || '%')
            ORDER BY updated_at DESC, id
            OFFSET @offset LIMIT @limit;
            """);
        AddScopeParameters(command, scope);
        AddPageParameters(command, request);
        return await ReadPageAsync(command, request.PageSize, ReadRelease, cancellationToken);
    }

    public Task<DesktopRelease?> GetReleaseAsync(
        ReleaseScope scope,
        Guid releaseId,
        CancellationToken cancellationToken) =>
        GetReleaseCoreAsync(scope, "id = @lookup", releaseId, null, cancellationToken);

    public Task<DesktopRelease?> GetReleaseByVersionAsync(
        ReleaseScope scope,
        Guid channelId,
        string releaseVersion,
        CancellationToken cancellationToken) =>
        GetReleaseCoreAsync(
            scope,
            "channel_id = @channel_id AND release_version = @release_version",
            null,
            command =>
            {
                command.Parameters.AddWithValue("channel_id", channelId);
                command.Parameters.AddWithValue("release_version", releaseVersion);
            },
            cancellationToken);

    public async Task<bool> TryCreateReleaseAsync(
        DesktopRelease release,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO release.releases (
                id, tenant_id, application_id, environment_id, channel_id, release_version, display_name,
                release_notes, artifact_ids, rollout_basis_points, target_segment_id, mandatory,
                minimum_version, bucketing_salt, status, revision, manifest_payload_json, manifest_sha256,
                manifest_signature, manifest_signing_key_id, manifest_signing_key_fingerprint,
                manifest_generated_at, version, created_at, updated_at, published_at, paused_at, rolled_back_at)
            VALUES (
                @id, @tenant_id, @application_id, @environment_id, @channel_id, @release_version, @display_name,
                @release_notes, @artifact_ids, @rollout_basis_points, @target_segment_id, @mandatory,
                @minimum_version, @bucketing_salt, @status, @revision, @manifest_payload_json, @manifest_sha256,
                @manifest_signature, @manifest_signing_key_id, @manifest_signing_key_fingerprint,
                @manifest_generated_at, @version, @created_at, @updated_at, @published_at, @paused_at, @rolled_back_at)
            ON CONFLICT DO NOTHING;
            """);
        AddReleaseParameters(command, release);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateReleaseAsync(
        DesktopRelease release,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ReleaseUpdateSql());
        AddReleaseParameters(command, release);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryPublishReleaseAsync(
        DesktopRelease release,
        long expectedReleaseVersion,
        ReleaseChannel channel,
        long expectedChannelVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await UpdateReleaseAsync(
                connection,
                transaction,
                release,
                expectedReleaseVersion,
                cancellationToken)
            || !await UpdateChannelAsync(
                connection,
                transaction,
                channel,
                expectedChannelVersion,
                cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryRollbackReleaseAsync(
        DesktopRelease currentRelease,
        long expectedCurrentVersion,
        DesktopRelease targetRelease,
        long expectedTargetVersion,
        ReleaseChannel channel,
        long expectedChannelVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await UpdateReleaseAsync(
                connection,
                transaction,
                currentRelease,
                expectedCurrentVersion,
                cancellationToken)
            || !await UpdateReleaseAsync(
                connection,
                transaction,
                targetRelease,
                expectedTargetVersion,
                cancellationToken)
            || !await UpdateChannelAsync(
                connection,
                transaction,
                channel,
                expectedChannelVersion,
                cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<ReleaseSigningKey?> GetSigningKeyCoreAsync(
        Guid tenantId,
        Guid signingKeyId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {SigningKeyColumns} FROM release.signing_keys WHERE tenant_id = @tenant_id AND id = @id;");
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", signingKeyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSigningKey(reader) : null;
    }

    private async Task<ReleaseChannel?> GetChannelCoreAsync<T>(
        ReleaseScope scope,
        string predicate,
        T lookup,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {ChannelColumns} FROM release.channels "
            + "WHERE tenant_id = @tenant_id AND application_id = @application_id "
            + $"AND environment_id = @environment_id AND {predicate};");
        AddScopeParameters(command, scope);
        command.Parameters.AddWithValue("lookup", lookup!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadChannel(reader) : null;
    }

    private async Task<ReleaseArtifact?> GetArtifactCoreAsync(
        ReleaseScope scope,
        string predicate,
        object? lookup,
        Action<NpgsqlCommand>? configure,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {ArtifactColumns} FROM release.artifacts "
            + "WHERE tenant_id = @tenant_id AND application_id = @application_id "
            + $"AND environment_id = @environment_id AND {predicate};");
        AddScopeParameters(command, scope);
        if (lookup is not null)
        {
            command.Parameters.AddWithValue("lookup", lookup);
        }
        configure?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadArtifact(reader) : null;
    }

    private async Task<DesktopRelease?> GetReleaseCoreAsync(
        ReleaseScope scope,
        string predicate,
        object? lookup,
        Action<NpgsqlCommand>? configure,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {ReleaseColumns} FROM release.releases "
            + "WHERE tenant_id = @tenant_id AND application_id = @application_id "
            + $"AND environment_id = @environment_id AND {predicate};");
        AddScopeParameters(command, scope);
        if (lookup is not null)
        {
            command.Parameters.AddWithValue("lookup", lookup);
        }
        configure?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRelease(reader) : null;
    }

    private static async Task<bool> UpdateReleaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DesktopRelease release,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ReleaseUpdateSql();
        AddReleaseParameters(command, release);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<bool> UpdateChannelAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReleaseChannel channel,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ChannelUpdateSql();
        AddChannelParameters(command, channel);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<ReleaseStorePage<T>> ReadPageAsync<T>(
        NpgsqlCommand command,
        int pageSize,
        Func<NpgsqlDataReader, T> mapper,
        CancellationToken cancellationToken)
    {
        var items = new List<T>(pageSize + 1);
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

    private static void AddPageParameters(NpgsqlCommand command, ReleasePageRequest request)
    {
        command.Parameters.AddWithValue("include_inactive", request.IncludeInactive);
        command.Parameters.AddWithValue("query", request.Query);
        command.Parameters.AddWithValue("offset", request.Offset);
        command.Parameters.AddWithValue("limit", request.PageSize + 1);
    }

    private static void AddScopeParameters(NpgsqlCommand command, ReleaseScope scope)
    {
        command.Parameters.AddWithValue("tenant_id", scope.TenantId);
        command.Parameters.AddWithValue("application_id", scope.ApplicationId);
        command.Parameters.AddWithValue("environment_id", scope.EnvironmentId);
    }

    private static void AddSigningKeyParameters(NpgsqlCommand command, ReleaseSigningKey item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("tenant_id", item.TenantId);
        command.Parameters.AddWithValue("key", item.Key);
        command.Parameters.AddWithValue("display_name", item.DisplayName);
        command.Parameters.AddWithValue("algorithm", item.Algorithm);
        command.Parameters.AddWithValue("fingerprint", item.Fingerprint);
        command.Parameters.AddWithValue("public_key_pem", item.PublicKeyPem);
        command.Parameters.AddWithValue("status", (short)item.Status);
        command.Parameters.AddWithValue("version", item.Version);
        command.Parameters.AddWithValue("created_at", item.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", item.UpdatedAt.UtcDateTime);
        AddTimestamp(command, "archived_at", item.ArchivedAt);
    }

    private static void AddChannelParameters(NpgsqlCommand command, ReleaseChannel item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        AddScopeParameters(command, new(item.TenantId, item.ApplicationId, item.EnvironmentId));
        command.Parameters.AddWithValue("key", item.Key);
        command.Parameters.AddWithValue("display_name", item.DisplayName);
        command.Parameters.AddWithValue("description", item.Description);
        command.Parameters.AddWithValue("status", (short)item.Status);
        AddGuid(command, "active_release_id", item.ActiveReleaseId);
        AddGuid(command, "previous_release_id", item.PreviousReleaseId);
        command.Parameters.AddWithValue("version", item.Version);
        command.Parameters.AddWithValue("created_at", item.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", item.UpdatedAt.UtcDateTime);
        AddTimestamp(command, "archived_at", item.ArchivedAt);
    }

    private static void AddArtifactParameters(NpgsqlCommand command, ReleaseArtifact item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        AddScopeParameters(command, new(item.TenantId, item.ApplicationId, item.EnvironmentId));
        command.Parameters.AddWithValue("release_version", item.ReleaseVersion);
        command.Parameters.AddWithValue("target_runtime_id", item.TargetRuntimeId);
        command.Parameters.AddWithValue("artifact_kind", (short)item.ArtifactKind);
        command.Parameters.AddWithValue("delta_from_version", item.DeltaFromVersion);
        command.Parameters.AddWithValue("file_name", item.FileName);
        command.Parameters.AddWithValue("content_type", item.ContentType);
        command.Parameters.AddWithValue("size_bytes", item.SizeBytes);
        command.Parameters.AddWithValue("sha256", item.Sha256);
        command.Parameters.AddWithValue("signing_key_id", item.SigningKeyId);
        command.Parameters.AddWithValue("signature", item.Signature);
        command.Parameters.AddWithValue("status", (short)item.Status);
        command.Parameters.AddWithValue("failure_reason", item.FailureReason);
        command.Parameters.AddWithValue("storage_bucket_id", item.StorageBucketId);
        command.Parameters.AddWithValue("storage_object_id", item.StorageObjectId);
        command.Parameters.AddWithValue("upload_session_id", item.UploadSessionId);
        command.Parameters.AddWithValue("storage_object_version", item.StorageObjectVersion);
        command.Parameters.AddWithValue("version", item.Version);
        command.Parameters.AddWithValue("created_at", item.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", item.UpdatedAt.UtcDateTime);
        AddTimestamp(command, "verified_at", item.VerifiedAt);
        AddTimestamp(command, "archived_at", item.ArchivedAt);
    }

    private static void AddReleaseParameters(NpgsqlCommand command, DesktopRelease item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        AddScopeParameters(command, new(item.TenantId, item.ApplicationId, item.EnvironmentId));
        command.Parameters.AddWithValue("channel_id", item.ChannelId);
        command.Parameters.AddWithValue("release_version", item.ReleaseVersion);
        command.Parameters.AddWithValue("display_name", item.DisplayName);
        command.Parameters.AddWithValue("release_notes", item.ReleaseNotes);
        command.Parameters.Add(new NpgsqlParameter("artifact_ids", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(item.ArtifactIds, JsonOptions),
        });
        command.Parameters.AddWithValue("rollout_basis_points", checked((int)item.RolloutBasisPoints));
        AddGuid(command, "target_segment_id", item.TargetSegmentId);
        command.Parameters.AddWithValue("mandatory", item.Mandatory);
        command.Parameters.AddWithValue("minimum_version", item.MinimumVersion);
        command.Parameters.AddWithValue("bucketing_salt", item.BucketingSalt);
        command.Parameters.AddWithValue("status", (short)item.Status);
        command.Parameters.AddWithValue("revision", item.Revision);
        command.Parameters.AddWithValue("manifest_payload_json", item.ManifestPayloadJson);
        command.Parameters.AddWithValue("manifest_sha256", item.ManifestSha256);
        command.Parameters.AddWithValue("manifest_signature", item.ManifestSignature);
        AddGuid(command, "manifest_signing_key_id", item.ManifestSigningKeyId);
        command.Parameters.AddWithValue(
            "manifest_signing_key_fingerprint",
            item.ManifestSigningKeyFingerprint);
        AddTimestamp(command, "manifest_generated_at", item.ManifestGeneratedAt);
        command.Parameters.AddWithValue("version", item.Version);
        command.Parameters.AddWithValue("created_at", item.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", item.UpdatedAt.UtcDateTime);
        AddTimestamp(command, "published_at", item.PublishedAt);
        AddTimestamp(command, "paused_at", item.PausedAt);
        AddTimestamp(command, "rolled_back_at", item.RolledBackAt);
    }

    private static void AddGuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid)
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

    private static ReleaseSigningKey ReadSigningKey(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            (ReleaseSigningKeyStatus)reader.GetInt16(7),
            reader.GetInt64(8),
            ToDateTimeOffset(reader.GetDateTime(9)),
            ToDateTimeOffset(reader.GetDateTime(10)),
            reader.IsDBNull(11) ? null : ToDateTimeOffset(reader.GetDateTime(11)));

    private static ReleaseChannel ReadChannel(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            (ReleaseChannelStatus)reader.GetInt16(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.GetInt64(10),
            ToDateTimeOffset(reader.GetDateTime(11)),
            ToDateTimeOffset(reader.GetDateTime(12)),
            reader.IsDBNull(13) ? null : ToDateTimeOffset(reader.GetDateTime(13)));

    private static ReleaseArtifact ReadArtifact(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            (ReleaseArtifactKind)reader.GetInt16(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetInt64(10),
            reader.GetString(11),
            reader.GetGuid(12),
            reader.GetString(13),
            (ReleaseArtifactStatus)reader.GetInt16(14),
            reader.GetString(15),
            reader.GetGuid(16),
            reader.GetGuid(17),
            reader.GetGuid(18),
            reader.GetInt64(19),
            reader.GetInt64(20),
            ToDateTimeOffset(reader.GetDateTime(21)),
            ToDateTimeOffset(reader.GetDateTime(22)),
            reader.IsDBNull(23) ? null : ToDateTimeOffset(reader.GetDateTime(23)),
            reader.IsDBNull(24) ? null : ToDateTimeOffset(reader.GetDateTime(24)));

    private static DesktopRelease ReadRelease(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            JsonSerializer.Deserialize<Guid[]>(reader.GetString(8), JsonOptions) ?? [],
            checked((uint)reader.GetInt32(9)),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.GetBoolean(11),
            reader.GetString(12),
            reader.GetString(13),
            (DesktopReleaseStatus)reader.GetInt16(14),
            reader.GetInt64(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetString(18),
            reader.IsDBNull(19) ? null : reader.GetGuid(19),
            reader.GetString(20),
            reader.IsDBNull(21) ? null : ToDateTimeOffset(reader.GetDateTime(21)),
            reader.GetInt64(22),
            ToDateTimeOffset(reader.GetDateTime(23)),
            ToDateTimeOffset(reader.GetDateTime(24)),
            reader.IsDBNull(25) ? null : ToDateTimeOffset(reader.GetDateTime(25)),
            reader.IsDBNull(26) ? null : ToDateTimeOffset(reader.GetDateTime(26)),
            reader.IsDBNull(27) ? null : ToDateTimeOffset(reader.GetDateTime(27)));

    private static string SigningKeyUpdateSql() =>
        """
        UPDATE release.signing_keys
        SET display_name = @display_name,
            status = @status,
            version = @version,
            updated_at = @updated_at,
            archived_at = @archived_at
        WHERE id = @id
          AND tenant_id = @tenant_id
          AND key = @key
          AND fingerprint = @fingerprint
          AND version = @expected_version;
        """;

    private static string ChannelUpdateSql() =>
        """
        UPDATE release.channels
        SET display_name = @display_name,
            description = @description,
            status = @status,
            active_release_id = @active_release_id,
            previous_release_id = @previous_release_id,
            version = @version,
            updated_at = @updated_at,
            archived_at = @archived_at
        WHERE id = @id
          AND tenant_id = @tenant_id
          AND application_id = @application_id
          AND environment_id = @environment_id
          AND key = @key
          AND version = @expected_version;
        """;

    private static string ArtifactUpdateSql() =>
        """
        UPDATE release.artifacts
        SET status = @status,
            failure_reason = @failure_reason,
            storage_object_version = @storage_object_version,
            version = @version,
            updated_at = @updated_at,
            verified_at = @verified_at,
            archived_at = @archived_at
        WHERE id = @id
          AND tenant_id = @tenant_id
          AND application_id = @application_id
          AND environment_id = @environment_id
          AND storage_object_id = @storage_object_id
          AND upload_session_id = @upload_session_id
          AND version = @expected_version;
        """;

    private static string ReleaseUpdateSql() =>
        """
        UPDATE release.releases
        SET display_name = @display_name,
            release_notes = @release_notes,
            artifact_ids = @artifact_ids,
            rollout_basis_points = @rollout_basis_points,
            target_segment_id = @target_segment_id,
            mandatory = @mandatory,
            minimum_version = @minimum_version,
            status = @status,
            revision = @revision,
            manifest_payload_json = @manifest_payload_json,
            manifest_sha256 = @manifest_sha256,
            manifest_signature = @manifest_signature,
            manifest_signing_key_id = @manifest_signing_key_id,
            manifest_signing_key_fingerprint = @manifest_signing_key_fingerprint,
            manifest_generated_at = @manifest_generated_at,
            version = @version,
            updated_at = @updated_at,
            published_at = @published_at,
            paused_at = @paused_at,
            rolled_back_at = @rolled_back_at
        WHERE id = @id
          AND tenant_id = @tenant_id
          AND application_id = @application_id
          AND environment_id = @environment_id
          AND channel_id = @channel_id
          AND release_version = @release_version
          AND version = @expected_version;
        """;

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
