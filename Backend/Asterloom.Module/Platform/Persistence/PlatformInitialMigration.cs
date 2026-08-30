using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Platform.Persistence;

public sealed class PlatformInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "platform";

    public int Version => 1;

    public string Name => "initial_platform_resources";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS platform;

        CREATE TABLE platform.tenants (
            id uuid PRIMARY KEY,
            slug text NOT NULL,
            display_name text NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT tenants_slug_format CHECK (slug ~ '^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$'),
            CONSTRAINT tenants_version_positive CHECK (version > 0),
            CONSTRAINT tenants_slug_unique UNIQUE (slug)
        );

        CREATE TABLE platform.applications (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            slug text NOT NULL,
            display_name text NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT applications_tenant_fk
                FOREIGN KEY (tenant_id) REFERENCES platform.tenants (id),
            CONSTRAINT applications_slug_format CHECK (slug ~ '^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$'),
            CONSTRAINT applications_version_positive CHECK (version > 0),
            CONSTRAINT applications_tenant_slug_unique UNIQUE (tenant_id, slug),
            CONSTRAINT applications_tenant_id_unique UNIQUE (tenant_id, id)
        );

        CREATE INDEX applications_tenant_status_idx
            ON platform.applications (tenant_id, status, display_name);

        CREATE TABLE platform.environments (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            slug text NOT NULL,
            display_name text NOT NULL,
            environment_type smallint NOT NULL,
            is_protected boolean NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT environments_application_fk
                FOREIGN KEY (tenant_id, application_id)
                REFERENCES platform.applications (tenant_id, id),
            CONSTRAINT environments_slug_format CHECK (slug ~ '^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$'),
            CONSTRAINT environments_version_positive CHECK (version > 0),
            CONSTRAINT environments_application_slug_unique UNIQUE (application_id, slug)
        );

        CREATE INDEX environments_tenant_application_status_idx
            ON platform.environments (tenant_id, application_id, status, display_name);

        CREATE TABLE platform.tenant_memberships (
            tenant_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id, actor_id),
            CONSTRAINT tenant_memberships_tenant_fk
                FOREIGN KEY (tenant_id) REFERENCES platform.tenants (id),
            CONSTRAINT tenant_memberships_version_positive CHECK (version > 0)
        );

        CREATE INDEX tenant_memberships_actor_status_idx
            ON platform.tenant_memberships (actor_id, status, tenant_id);
        """;
}
