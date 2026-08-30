using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Release.Persistence;

public sealed class ReleaseInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "release";

    public int Version => 1;

    public string Name => "initial_desktop_release";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS release;

        CREATE TABLE release.signing_keys (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            key text NOT NULL,
            display_name text NOT NULL,
            algorithm text NOT NULL,
            fingerprint text NOT NULL,
            public_key_pem text NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT release_signing_keys_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT release_signing_keys_version_positive CHECK (version > 0),
            CONSTRAINT release_signing_keys_tenant_key_unique UNIQUE (tenant_id, key),
            CONSTRAINT release_signing_keys_tenant_fingerprint_unique
                UNIQUE (tenant_id, fingerprint)
        );

        CREATE INDEX release_signing_keys_tenant_status_name_idx
            ON release.signing_keys (tenant_id, status, lower(display_name), id);

        CREATE TABLE release.channels (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            key text NOT NULL,
            display_name text NOT NULL,
            description text NOT NULL,
            status smallint NOT NULL,
            active_release_id uuid NULL,
            previous_release_id uuid NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT release_channels_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT release_channels_version_positive CHECK (version > 0),
            CONSTRAINT release_channels_scope_key_unique
                UNIQUE (tenant_id, application_id, environment_id, key)
        );

        CREATE INDEX release_channels_scope_status_name_idx
            ON release.channels (
                tenant_id, application_id, environment_id, status, lower(display_name), id);

        CREATE TABLE release.artifacts (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            release_version text NOT NULL,
            target_runtime_id text NOT NULL,
            artifact_kind smallint NOT NULL,
            delta_from_version text NOT NULL,
            file_name text NOT NULL,
            content_type text NOT NULL,
            size_bytes bigint NOT NULL,
            sha256 text NOT NULL,
            signing_key_id uuid NOT NULL REFERENCES release.signing_keys (id),
            signature text NOT NULL,
            status smallint NOT NULL,
            failure_reason text NOT NULL,
            storage_bucket_id uuid NOT NULL,
            storage_object_id uuid NOT NULL,
            upload_session_id uuid NOT NULL,
            storage_object_version bigint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            verified_at timestamptz NULL,
            archived_at timestamptz NULL,
            CONSTRAINT release_artifacts_kind_valid CHECK (artifact_kind IN (1, 2)),
            CONSTRAINT release_artifacts_status_valid CHECK (status IN (1, 2, 3, 4)),
            CONSTRAINT release_artifacts_sizes_positive CHECK (size_bytes > 0),
            CONSTRAINT release_artifacts_versions_positive CHECK (
                storage_object_version > 0 AND version > 0),
            CONSTRAINT release_artifacts_scope_identity_unique UNIQUE (
                tenant_id, application_id, environment_id, release_version,
                target_runtime_id, artifact_kind, delta_from_version),
            CONSTRAINT release_artifacts_storage_object_unique UNIQUE (storage_object_id),
            CONSTRAINT release_artifacts_upload_session_unique UNIQUE (upload_session_id)
        );

        CREATE INDEX release_artifacts_scope_status_version_idx
            ON release.artifacts (
                tenant_id, application_id, environment_id, status,
                release_version, target_runtime_id, id);

        CREATE TABLE release.releases (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            channel_id uuid NOT NULL REFERENCES release.channels (id),
            release_version text NOT NULL,
            display_name text NOT NULL,
            release_notes text NOT NULL,
            artifact_ids jsonb NOT NULL,
            rollout_basis_points integer NOT NULL,
            target_segment_id uuid NULL,
            mandatory boolean NOT NULL,
            minimum_version text NOT NULL,
            bucketing_salt text NOT NULL,
            status smallint NOT NULL,
            revision bigint NOT NULL,
            manifest_payload_json text NOT NULL,
            manifest_sha256 text NOT NULL,
            manifest_signature text NOT NULL,
            manifest_signing_key_id uuid NULL REFERENCES release.signing_keys (id),
            manifest_signing_key_fingerprint text NOT NULL,
            manifest_generated_at timestamptz NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            published_at timestamptz NULL,
            paused_at timestamptz NULL,
            rolled_back_at timestamptz NULL,
            CONSTRAINT desktop_releases_status_valid CHECK (status IN (1, 2, 3, 4)),
            CONSTRAINT desktop_releases_rollout_valid CHECK (
                rollout_basis_points BETWEEN 1 AND 100000),
            CONSTRAINT desktop_releases_versions_positive CHECK (revision > 0 AND version > 0),
            CONSTRAINT desktop_releases_scope_version_unique UNIQUE (
                tenant_id, application_id, environment_id, channel_id, release_version)
        );

        CREATE INDEX desktop_releases_scope_status_updated_idx
            ON release.releases (
                tenant_id, application_id, environment_id, status, updated_at DESC, id);

        ALTER TABLE release.channels
            ADD CONSTRAINT release_channels_active_release_fk
                FOREIGN KEY (active_release_id) REFERENCES release.releases (id),
            ADD CONSTRAINT release_channels_previous_release_fk
                FOREIGN KEY (previous_release_id) REFERENCES release.releases (id);
        """;
}
