using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Authorization.Persistence;

public sealed class AuthorizationApplicationPolicyMigration : IAsterloomModuleMigration
{
    public string ModuleName => "authorization";

    public int Version => 2;

    public string Name => "application_permissions_acl_abac";

    public string Sql =>
        """
        CREATE TABLE "authorization".permissions (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            key text NOT NULL,
            display_name text NOT NULL,
            description text NOT NULL,
            module text NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT authorization_permissions_identity_unique
                UNIQUE (tenant_id, application_id, key),
            CONSTRAINT authorization_permissions_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT authorization_permissions_version_positive CHECK (version > 0)
        );

        CREATE INDEX authorization_permissions_scope_status_key_idx
            ON "authorization".permissions (
                tenant_id, application_id, status, key);

        ALTER TABLE "authorization".roles
            ADD COLUMN tenant_id uuid NULL,
            ADD COLUMN application_id uuid NULL;

        ALTER TABLE "authorization".roles
            DROP CONSTRAINT authorization_roles_key_unique;

        ALTER TABLE "authorization".roles
            ADD CONSTRAINT authorization_roles_scope_nested CHECK (
                application_id IS NULL OR tenant_id IS NOT NULL),
            ADD CONSTRAINT authorization_roles_scope_key_unique
                UNIQUE NULLS NOT DISTINCT (tenant_id, application_id, key);

        CREATE INDEX authorization_roles_scope_status_name_idx
            ON "authorization".roles (
                tenant_id, application_id, status, lower(display_name), id);

        ALTER TABLE "authorization".policy_rules
            ADD COLUMN resource_type text NOT NULL DEFAULT '',
            ADD COLUMN resource_id text NOT NULL DEFAULT '',
            ADD COLUMN condition jsonb NULL,
            ADD CONSTRAINT authorization_policy_rules_resource_nested CHECK (
                resource_id = '' OR resource_type <> '');

        CREATE INDEX authorization_policy_rules_resource_idx
            ON "authorization".policy_rules (
                tenant_id, application_id, resource_type, resource_id, status);
        """;
}
