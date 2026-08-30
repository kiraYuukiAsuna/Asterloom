using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Storage.Persistence;

public sealed class StorageInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "storage";

    public int Version => 1;

    public string Name => "initial_storage";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS storage;

        CREATE TABLE storage.buckets (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            key text NOT NULL,
            display_name text NOT NULL,
            description text NOT NULL,
            quota_bytes bigint NOT NULL,
            max_object_size_bytes bigint NOT NULL,
            allowed_content_types jsonb NOT NULL,
            access_policy smallint NOT NULL,
            status smallint NOT NULL,
            used_bytes bigint NOT NULL,
            reserved_bytes bigint NOT NULL,
            object_count bigint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT storage_buckets_scope_key_unique UNIQUE (tenant_id, key),
            CONSTRAINT storage_buckets_sizes_valid CHECK (
                quota_bytes > 0 AND max_object_size_bytes > 0
                AND max_object_size_bytes <= quota_bytes),
            CONSTRAINT storage_buckets_counters_valid CHECK (
                used_bytes >= 0 AND reserved_bytes >= 0 AND object_count >= 0),
            CONSTRAINT storage_buckets_access_policy_valid CHECK (access_policy IN (1, 2)),
            CONSTRAINT storage_buckets_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT storage_buckets_version_positive CHECK (version > 0)
        );

        CREATE INDEX storage_buckets_tenant_status_name_idx
            ON storage.buckets (tenant_id, status, lower(display_name), id);

        CREATE TABLE storage.objects (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            bucket_id uuid NOT NULL REFERENCES storage.buckets (id),
            application_id uuid NULL,
            environment_id uuid NULL,
            object_key text NOT NULL,
            physical_key text NOT NULL,
            file_name text NOT NULL,
            content_type text NOT NULL,
            size_bytes bigint NOT NULL,
            sha256 text NOT NULL,
            custom_metadata jsonb NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            completed_at timestamptz NULL,
            deleted_at timestamptz NULL,
            CONSTRAINT storage_objects_scope_key_unique UNIQUE (tenant_id, bucket_id, object_key),
            CONSTRAINT storage_objects_size_valid CHECK (size_bytes >= 0),
            CONSTRAINT storage_objects_status_valid CHECK (status IN (1, 2, 3, 4)),
            CONSTRAINT storage_objects_version_positive CHECK (version > 0)
        );

        CREATE INDEX storage_objects_bucket_status_name_idx
            ON storage.objects (tenant_id, bucket_id, status, lower(file_name), id);

        CREATE TABLE storage.upload_sessions (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            bucket_id uuid NOT NULL,
            object_id uuid NOT NULL REFERENCES storage.objects (id),
            transfer jsonb NOT NULL,
            status smallint NOT NULL,
            failure_reason text NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            expires_at timestamptz NOT NULL,
            completed_at timestamptz NULL,
            CONSTRAINT storage_upload_sessions_status_valid CHECK (status IN (1, 2, 3, 4, 5)),
            CONSTRAINT storage_upload_sessions_version_positive CHECK (version > 0)
        );

        CREATE INDEX storage_upload_sessions_scope_idx
            ON storage.upload_sessions (tenant_id, bucket_id, object_id, created_at DESC);
        """;
}
