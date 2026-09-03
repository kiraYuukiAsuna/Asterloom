using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Telemetry.Persistence;

public sealed class TelemetryRecordsMigration : IAsterloomModuleMigration
{
    public string ModuleName => "telemetry";

    public int Version => 2;

    public string Name => "database_records";

    public string Sql =>
        """
        CREATE TABLE telemetry.records (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            signal_type smallint NOT NULL,
            service_name text NOT NULL,
            observed_at timestamptz NOT NULL,
            trace_id text NOT NULL,
            span_id text NOT NULL,
            name text NOT NULL,
            category text NOT NULL,
            value text NOT NULL,
            duration_milliseconds double precision NULL,
            attributes_json jsonb NOT NULL,
            payload_json jsonb NOT NULL,
            created_at timestamptz NOT NULL,
            CONSTRAINT telemetry_record_signal_valid CHECK (signal_type IN (1, 2, 3)),
            CONSTRAINT telemetry_record_duration_valid CHECK (
                duration_milliseconds IS NULL OR duration_milliseconds >= 0)
        );

        CREATE INDEX telemetry_record_scope_signal_time_idx
            ON telemetry.records (
                tenant_id, application_id, environment_id, signal_type,
                observed_at DESC, id DESC);
        CREATE INDEX telemetry_record_scope_service_time_idx
            ON telemetry.records (
                tenant_id, application_id, environment_id, service_name,
                observed_at DESC, id DESC);
        CREATE INDEX telemetry_record_trace_idx
            ON telemetry.records (tenant_id, application_id, environment_id, trace_id)
            WHERE trace_id <> '';
        CREATE INDEX telemetry_record_retention_idx
            ON telemetry.records (observed_at);
        """;
}
