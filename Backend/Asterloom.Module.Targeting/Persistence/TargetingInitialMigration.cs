using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Targeting.Persistence;

public sealed class TargetingInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "targeting";

    public int Version => 1;

    public string Name => "initial_targeting_segments";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS targeting;

        CREATE UNIQUE INDEX IF NOT EXISTS platform_environments_scope_id_unique
            ON platform.environments (tenant_id, application_id, id);

        CREATE TABLE targeting.segments (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            key text NOT NULL,
            display_name text NOT NULL,
            description text NOT NULL,
            rule jsonb NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT targeting_segments_environment_fk
                FOREIGN KEY (tenant_id, application_id, environment_id)
                REFERENCES platform.environments (tenant_id, application_id, id),
            CONSTRAINT targeting_segments_key_format CHECK (
                key ~ '^[a-z0-9]([a-z0-9._-]{0,98}[a-z0-9])?$'),
            CONSTRAINT targeting_segments_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT targeting_segments_version_positive CHECK (version > 0),
            CONSTRAINT targeting_segments_scope_key_unique
                UNIQUE (tenant_id, application_id, environment_id, key)
        );

        CREATE INDEX targeting_segments_scope_status_name_idx
            ON targeting.segments (
                tenant_id, application_id, environment_id, status, lower(display_name), id);
        """;
}
