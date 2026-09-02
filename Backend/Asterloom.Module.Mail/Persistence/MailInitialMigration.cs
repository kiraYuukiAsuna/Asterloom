using Asterloom.Modules.Persistence;

namespace Asterloom.Modules.Mail.Persistence;

public sealed class MailInitialMigration : IAsterloomModuleMigration
{
    public string ModuleName => "mail";

    public int Version => 1;

    public string Name => "initial_mail";

    public string Sql =>
        """
        CREATE SCHEMA IF NOT EXISTS mail;

        CREATE TABLE mail.smtp_accounts (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            name text NOT NULL,
            host text NOT NULL,
            port integer NOT NULL,
            security smallint NOT NULL,
            username text NOT NULL,
            credential_ciphertext text NOT NULL,
            from_address text NOT NULL,
            from_name text NOT NULL,
            status smallint NOT NULL,
            version bigint NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            archived_at timestamptz NULL,
            CONSTRAINT mail_smtp_account_port_valid CHECK (port BETWEEN 1 AND 65535),
            CONSTRAINT mail_smtp_account_security_valid CHECK (security IN (1, 2)),
            CONSTRAINT mail_smtp_account_status_valid CHECK (status IN (1, 2)),
            CONSTRAINT mail_smtp_account_version_positive CHECK (version > 0)
        );

        CREATE UNIQUE INDEX mail_smtp_account_scope_name_unique
            ON mail.smtp_accounts (tenant_id, application_id, lower(name));
        CREATE INDEX mail_smtp_account_scope_status_name_idx
            ON mail.smtp_accounts (
                tenant_id, application_id, status, lower(name), id);

        CREATE TABLE mail.deliveries (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            application_id uuid NOT NULL,
            smtp_account_id uuid NOT NULL,
            client_message_id text NOT NULL,
            recipients_to text[] NOT NULL,
            recipients_cc text[] NOT NULL,
            recipients_bcc text[] NOT NULL,
            reply_to text NOT NULL,
            subject text NOT NULL,
            status smallint NOT NULL,
            provider_message_id text NOT NULL,
            error_code text NOT NULL,
            error_message text NOT NULL,
            created_at timestamptz NOT NULL,
            completed_at timestamptz NULL,
            CONSTRAINT mail_delivery_status_valid CHECK (status IN (1, 2, 3)),
            CONSTRAINT mail_delivery_account_fk FOREIGN KEY (smtp_account_id)
                REFERENCES mail.smtp_accounts (id),
            CONSTRAINT mail_delivery_client_message_unique
                UNIQUE (tenant_id, application_id, client_message_id)
        );

        CREATE INDEX mail_delivery_scope_created_idx
            ON mail.deliveries (
                tenant_id, application_id, created_at DESC, id DESC);
        CREATE INDEX mail_delivery_scope_status_created_idx
            ON mail.deliveries (
                tenant_id, application_id, status, created_at DESC, id DESC);
        """;
}
