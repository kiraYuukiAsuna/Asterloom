using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Auditing;

public sealed class AuditInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "audit";

    public int Version => 1;

    public string Name => "initial_audit_events";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS infrastructure;

        CREATE TABLE infrastructure.audit_events (
            id uuid PRIMARY KEY,
            actor_id text NOT NULL,
            tenant_id uuid NULL,
            application_id uuid NULL,
            environment_id uuid NULL,
            operation text NOT NULL,
            resource_type text NOT NULL,
            resource_id text NOT NULL,
            request_id text NOT NULL,
            outcome smallint NOT NULL,
            error_code text NOT NULL,
            change_summary text NOT NULL,
            created_at timestamptz NOT NULL,
            CONSTRAINT audit_events_scope_nested CHECK (
                (environment_id IS NULL OR application_id IS NOT NULL)
                AND (application_id IS NULL OR tenant_id IS NOT NULL)),
            CONSTRAINT audit_events_outcome_valid CHECK (outcome IN (1, 2, 3))
        );

        CREATE INDEX audit_events_created_at_idx
            ON infrastructure.audit_events (created_at DESC, id DESC);
        CREATE INDEX audit_events_actor_created_idx
            ON infrastructure.audit_events (actor_id, created_at DESC);
        CREATE INDEX audit_events_operation_created_idx
            ON infrastructure.audit_events (operation, created_at DESC);
        CREATE INDEX audit_events_request_id_idx
            ON infrastructure.audit_events (request_id);

        REVOKE UPDATE, DELETE, TRUNCATE ON infrastructure.audit_events FROM PUBLIC;
        """;
}
