using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Analytics.Persistence;

public sealed class AnalyticsInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "analytics";

    public int Version => 1;

    public string Name => "initial_analytics";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS analytics;

        CREATE TABLE analytics.event_schemas (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            key text NOT NULL,
            display_name text NOT NULL,
            description text NOT NULL,
            schema_json jsonb NOT NULL,
            status smallint NOT NULL,
            retention_days integer NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT analytics_schema_key_format CHECK (
                key ~ '^[a-z][a-z0-9]([a-z0-9._-]{0,98}[a-z0-9])?$'),
            CONSTRAINT analytics_schema_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT analytics_schema_retention_valid CHECK (retention_days BETWEEN 1 AND 3650),
            CONSTRAINT analytics_schema_version_positive CHECK (version > 0),
            CONSTRAINT analytics_schema_scope_key_unique
                UNIQUE (tenant_id, application_id, environment_id, key)
        );

        CREATE INDEX analytics_schema_scope_status_name_idx
            ON analytics.event_schemas (
                tenant_id, application_id, environment_id, status, lower(display_name), id);

        CREATE TABLE analytics.write_keys (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            name text NOT NULL,
            prefix text NOT NULL UNIQUE,
            secret_hash bytea NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            last_used_at timestamptz NULL,
            revoked_at timestamptz NULL,
            CONSTRAINT analytics_write_key_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT analytics_write_key_version_positive CHECK (version > 0)
        );

        CREATE INDEX analytics_write_key_scope_status_idx
            ON analytics.write_keys (
                tenant_id, application_id, environment_id, status, lower(name), id);

        CREATE TABLE analytics.event_deduplication (
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            event_id text NOT NULL,
            analytics_event_id uuid NOT NULL,
            received_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id, application_id, environment_id, event_id)
        );

        CREATE TABLE analytics.events (
            id uuid NOT NULL,
            event_id text NOT NULL,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            event_schema_id uuid NOT NULL,
            event_name text NOT NULL,
            schema_version bigint NOT NULL,
            occurred_at timestamptz NOT NULL,
            received_at timestamptz NOT NULL,
            actor_id text NOT NULL,
            anonymous_id text NOT NULL,
            session_id text NOT NULL,
            properties_json jsonb NOT NULL,
            context_json jsonb NOT NULL,
            sdk_name text NOT NULL,
            sdk_version text NOT NULL,
            write_key_prefix text NOT NULL,
            PRIMARY KEY (id, received_at)
        ) PARTITION BY RANGE (received_at);

        CREATE TABLE analytics.events_default
            PARTITION OF analytics.events DEFAULT;

        CREATE INDEX analytics_events_scope_received_idx
            ON analytics.events (
                tenant_id, application_id, environment_id, received_at DESC, id DESC);
        CREATE INDEX analytics_events_scope_name_received_idx
            ON analytics.events (
                tenant_id, application_id, environment_id, event_name, received_at DESC);
        CREATE INDEX analytics_events_actor_received_idx
            ON analytics.events (
                tenant_id, application_id, environment_id, actor_id, received_at DESC)
            WHERE actor_id <> '';
        """;
}
