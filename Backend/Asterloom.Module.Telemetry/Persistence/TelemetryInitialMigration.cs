using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Telemetry.Persistence;

public sealed class TelemetryInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "telemetry";

    public int Version => 1;

    public string Name => "initial_telemetry";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS telemetry;

        CREATE TABLE telemetry.sources (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            key text NOT NULL,
            display_name text NOT NULL,
            description text NOT NULL,
            service_name text NOT NULL,
            resource_attributes_json jsonb NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT telemetry_source_key_format CHECK (
                key ~ '^[a-z][a-z0-9]([a-z0-9._-]{0,98}[a-z0-9])?$'),
            CONSTRAINT telemetry_source_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT telemetry_source_version_positive CHECK (version > 0),
            CONSTRAINT telemetry_source_scope_key_unique
                UNIQUE (tenant_id, application_id, environment_id, key),
            CONSTRAINT telemetry_source_scope_service_unique
                UNIQUE (tenant_id, application_id, environment_id, service_name)
        );

        CREATE INDEX telemetry_source_scope_status_name_idx
            ON telemetry.sources (
                tenant_id, application_id, environment_id, status, lower(display_name), id);

        CREATE TABLE telemetry.settings (
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            sampling_ratio double precision NOT NULL,
            traces_enabled boolean NOT NULL,
            metrics_enabled boolean NOT NULL,
            logs_enabled boolean NOT NULL,
            exporter_endpoint text NOT NULL,
            exporter_protocol smallint NOT NULL,
            diagnostics_base_url text NOT NULL,
            version bigint NOT NULL,
            updated_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id, application_id, environment_id),
            CONSTRAINT telemetry_sampling_ratio_valid CHECK (
                sampling_ratio >= 0 AND sampling_ratio <= 1),
            CONSTRAINT telemetry_exporter_protocol_valid CHECK (exporter_protocol IN (1, 2)),
            CONSTRAINT telemetry_settings_version_positive CHECK (version > 0)
        );

        CREATE TABLE telemetry.recent_errors (
            id uuid PRIMARY KEY,
            tenant_id uuid NULL,
            application_id uuid NULL,
            environment_id uuid NULL,
            service_name text NOT NULL,
            exception_type text NOT NULL,
            message text NOT NULL,
            grpc_method text NOT NULL,
            trace_id text NOT NULL,
            span_id text NOT NULL,
            request_id text NOT NULL,
            occurred_at timestamptz NOT NULL
        );

        CREATE INDEX telemetry_recent_errors_scope_occurred_idx
            ON telemetry.recent_errors (
                tenant_id, application_id, environment_id, occurred_at DESC, id DESC);
        CREATE INDEX telemetry_recent_errors_trace_idx
            ON telemetry.recent_errors (trace_id)
            WHERE trace_id <> '';
        """;
}
