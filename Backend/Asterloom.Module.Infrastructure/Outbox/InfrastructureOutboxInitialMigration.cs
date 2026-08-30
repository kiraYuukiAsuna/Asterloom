using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Infrastructure.Outbox;

public sealed class InfrastructureOutboxInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "infrastructure";

    public int Version => 1;

    public string Name => "transactional_outbox";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS infrastructure;

        CREATE TABLE infrastructure.outbox_messages (
            id uuid PRIMARY KEY,
            event_type text NOT NULL,
            schema_version integer NOT NULL,
            payload jsonb NOT NULL,
            correlation_id text NOT NULL,
            tenant_id uuid NULL,
            application_id uuid NULL,
            environment_id uuid NULL,
            occurred_at timestamptz NOT NULL,
            available_at timestamptz NOT NULL,
            attempt_count integer NOT NULL DEFAULT 0,
            locked_by text NULL,
            locked_until timestamptz NULL,
            processed_at timestamptz NULL,
            dead_lettered_at timestamptz NULL,
            last_error text NOT NULL DEFAULT '',
            CONSTRAINT outbox_schema_version_positive CHECK (schema_version > 0),
            CONSTRAINT outbox_attempt_count_nonnegative CHECK (attempt_count >= 0),
            CONSTRAINT outbox_scope_nested CHECK (
                (environment_id IS NULL OR application_id IS NOT NULL)
                AND (application_id IS NULL OR tenant_id IS NOT NULL)),
            CONSTRAINT outbox_lock_complete CHECK (
                (locked_by IS NULL) = (locked_until IS NULL)),
            CONSTRAINT outbox_terminal_state CHECK (
                processed_at IS NULL OR dead_lettered_at IS NULL)
        );

        CREATE INDEX outbox_messages_dispatch_idx
            ON infrastructure.outbox_messages (available_at, occurred_at, id)
            WHERE processed_at IS NULL AND dead_lettered_at IS NULL;
        CREATE INDEX outbox_messages_event_type_idx
            ON infrastructure.outbox_messages (event_type, available_at)
            WHERE processed_at IS NULL AND dead_lettered_at IS NULL;

        CREATE TABLE infrastructure.inbox_receipts (
            event_id uuid NOT NULL,
            consumer_name text NOT NULL,
            processed_at timestamptz NOT NULL,
            PRIMARY KEY (event_id, consumer_name),
            CONSTRAINT inbox_receipts_event_fk
                FOREIGN KEY (event_id)
                REFERENCES infrastructure.outbox_messages (id)
                ON DELETE RESTRICT
        );
        """;
}
