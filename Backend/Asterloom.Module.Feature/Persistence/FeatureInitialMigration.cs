using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Feature.Persistence;

public sealed class FeatureInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "feature";

    public int Version => 1;

    public string Name => "initial_feature_flags";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS feature;

        CREATE TABLE feature.flags (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            key text NOT NULL,
            display_name text NOT NULL,
            description text NOT NULL,
            value_kind smallint NOT NULL,
            status smallint NOT NULL,
            draft_definition jsonb NOT NULL,
            draft_revision bigint NOT NULL,
            published_definition jsonb NULL,
            published_revision bigint NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            published_at timestamptz NULL,
            CONSTRAINT feature_flags_key_format CHECK (
                key ~ '^[a-z0-9]([a-z0-9._-]{0,98}[a-z0-9])?$'),
            CONSTRAINT feature_flags_value_kind_valid CHECK (value_kind IN (1, 2, 3, 4, 5)),
            CONSTRAINT feature_flags_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT feature_flags_revisions_positive CHECK (
                draft_revision > 0 AND (published_revision IS NULL OR published_revision > 0)),
            CONSTRAINT feature_flags_version_positive CHECK (version > 0),
            CONSTRAINT feature_flags_scope_key_unique
                UNIQUE (tenant_id, application_id, environment_id, key)
        );

        CREATE INDEX feature_flags_scope_status_name_idx
            ON feature.flags (
                tenant_id, application_id, environment_id, status, lower(display_name), id);

        CREATE TABLE feature.revisions (
            id uuid PRIMARY KEY,
            flag_id uuid NOT NULL REFERENCES feature.flags (id),
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            revision bigint NOT NULL,
            definition jsonb NOT NULL,
            source_revision bigint NULL,
            published_at timestamptz NOT NULL,
            CONSTRAINT feature_revisions_revision_positive CHECK (revision > 0),
            CONSTRAINT feature_revisions_flag_revision_unique UNIQUE (flag_id, revision)
        );

        CREATE INDEX feature_revisions_flag_revision_idx
            ON feature.revisions (flag_id, revision DESC);
        """;
}
