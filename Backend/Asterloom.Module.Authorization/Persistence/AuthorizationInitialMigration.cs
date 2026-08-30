using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Authorization.Persistence;

public sealed class AuthorizationInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "authorization";

    public int Version => 1;

    public string Name => "initial_authorization_resources";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS authorization;

        CREATE TABLE authorization.roles (
            id uuid PRIMARY KEY,
            key text NOT NULL,
            display_name text NOT NULL,
            description text NOT NULL,
            permissions text[] NOT NULL,
            is_system boolean NOT NULL DEFAULT false,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT authorization_roles_key_unique UNIQUE (key),
            CONSTRAINT authorization_roles_custom_only CHECK (NOT is_system),
            CONSTRAINT authorization_roles_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT authorization_roles_version_positive CHECK (version > 0),
            CONSTRAINT authorization_roles_permissions_present CHECK (cardinality(permissions) > 0)
        );

        CREATE INDEX authorization_roles_status_name_idx
            ON authorization.roles (status, lower(display_name), id);

        CREATE TABLE authorization.role_bindings (
            id uuid PRIMARY KEY,
            actor_id text NOT NULL,
            role_id uuid NOT NULL,
            role_key text NOT NULL,
            tenant_id uuid NULL,
            application_id uuid NULL,
            environment_id uuid NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT authorization_role_bindings_scope_nested CHECK (
                (environment_id IS NULL OR application_id IS NOT NULL)
                AND (application_id IS NULL OR tenant_id IS NOT NULL)),
            CONSTRAINT authorization_role_bindings_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT authorization_role_bindings_version_positive CHECK (version > 0),
            CONSTRAINT authorization_role_bindings_identity_unique
                UNIQUE NULLS NOT DISTINCT (
                    actor_id, role_id, tenant_id, application_id, environment_id)
        );

        CREATE INDEX authorization_role_bindings_actor_status_idx
            ON authorization.role_bindings (actor_id, status, tenant_id);
        CREATE INDEX authorization_role_bindings_role_status_idx
            ON authorization.role_bindings (role_id, status);

        CREATE TABLE authorization.policy_rules (
            id uuid PRIMARY KEY,
            name text NOT NULL,
            effect smallint NOT NULL,
            subject_type smallint NOT NULL,
            subject text NOT NULL,
            tenant_id uuid NULL,
            application_id uuid NULL,
            environment_id uuid NULL,
            permission text NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT authorization_policy_rules_scope_nested CHECK (
                (environment_id IS NULL OR application_id IS NOT NULL)
                AND (application_id IS NULL OR tenant_id IS NOT NULL)),
            CONSTRAINT authorization_policy_rules_effect_valid CHECK (effect IN (1, 2)),
            CONSTRAINT authorization_policy_rules_subject_type_valid CHECK (subject_type IN (1, 2, 3)),
            CONSTRAINT authorization_policy_rules_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT authorization_policy_rules_version_positive CHECK (version > 0)
        );

        CREATE INDEX authorization_policy_rules_scope_status_idx
            ON authorization.policy_rules (tenant_id, application_id, environment_id, status);
        CREATE INDEX authorization_policy_rules_subject_status_idx
            ON authorization.policy_rules (subject_type, subject, status);
        CREATE INDEX authorization_policy_rules_permission_status_idx
            ON authorization.policy_rules (permission, status);

        CREATE TABLE authorization.policy_revisions (
            id uuid PRIMARY KEY,
            revision_number bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
            change_type text NOT NULL,
            resource_type text NOT NULL,
            resource_id text NOT NULL,
            snapshot_hash text NOT NULL,
            change_summary text NOT NULL,
            created_by text NOT NULL,
            created_at timestamptz NOT NULL
        );

        CREATE INDEX authorization_policy_revisions_resource_idx
            ON authorization.policy_revisions (
                resource_type, resource_id, revision_number DESC);
        """;
}
