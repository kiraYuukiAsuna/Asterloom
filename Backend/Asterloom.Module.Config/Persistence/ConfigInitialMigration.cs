using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Config.Persistence;

public sealed class ConfigInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "config";

    public int Version => 1;

    public string Name => "initial_dynamic_config";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS config;

        CREATE TABLE config.entries (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            key text NOT NULL,
            display_name text NOT NULL,
            description text NOT NULL,
            value_kind smallint NOT NULL,
            visibility smallint NOT NULL,
            status smallint NOT NULL,
            draft_definition jsonb NOT NULL,
            draft_revision bigint NOT NULL,
            published_definition jsonb NULL,
            published_revision bigint NULL,
            published_snapshot_version bigint NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            published_at timestamptz NULL,
            CONSTRAINT config_entries_key_format CHECK (
                key ~ '^[a-z0-9]([a-z0-9._-]{0,98}[a-z0-9])?$'),
            CONSTRAINT config_entries_value_kind_valid CHECK (value_kind IN (1, 2, 3, 4, 5)),
            CONSTRAINT config_entries_visibility_valid CHECK (visibility IN (1, 2)),
            CONSTRAINT config_entries_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT config_entries_revisions_positive CHECK (
                draft_revision > 0
                AND (published_revision IS NULL OR published_revision > 0)
                AND (published_snapshot_version IS NULL OR published_snapshot_version > 0)),
            CONSTRAINT config_entries_version_positive CHECK (version > 0),
            CONSTRAINT config_entries_scope_key_unique
                UNIQUE (tenant_id, application_id, environment_id, key)
        );

        CREATE INDEX config_entries_scope_status_name_idx
            ON config.entries (
                tenant_id, application_id, environment_id, status, lower(display_name), id);

        CREATE TABLE config.revisions (
            id uuid PRIMARY KEY,
            entry_id uuid NOT NULL REFERENCES config.entries (id),
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            revision bigint NOT NULL,
            definition jsonb NOT NULL,
            source_revision bigint NULL,
            snapshot_version bigint NOT NULL,
            published_at timestamptz NOT NULL,
            CONSTRAINT config_revisions_revision_positive CHECK (
                revision > 0 AND snapshot_version > 0),
            CONSTRAINT config_revisions_entry_revision_unique UNIQUE (entry_id, revision)
        );

        CREATE INDEX config_revisions_entry_revision_idx
            ON config.revisions (entry_id, revision DESC);

        CREATE TABLE config.snapshots (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            version bigint NOT NULL,
            items jsonb NOT NULL,
            created_at timestamptz NOT NULL,
            CONSTRAINT config_snapshots_version_positive CHECK (version > 0),
            CONSTRAINT config_snapshots_scope_version_unique
                UNIQUE (tenant_id, application_id, environment_id, version)
        );

        CREATE INDEX config_snapshots_scope_version_idx
            ON config.snapshots (
                tenant_id, application_id, environment_id, version DESC);
        """;
}
