using Asterloom.Modules.Mail.Model;
using Asterloom.Modules.Mail.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Asterloom.Modules.Infrastructure.Mail;

internal sealed class PostgreSqlMailStore(NpgsqlDataSource dataSource) : IMailStore
{
    private const string AccountColumns =
        """
        id, tenant_id, application_id, name, host, port, security, username,
        credential_ciphertext, from_address, from_name, status, version,
        created_at, updated_at, archived_at
        """;

    private const string DeliveryColumns =
        """
        id, tenant_id, application_id, smtp_account_id, client_message_id,
        recipients_to, recipients_cc, recipients_bcc, reply_to, subject, status,
        provider_message_id, error_code, error_message, created_at, completed_at
        """;

    public async Task<MailPage<SmtpAccount>> ListAccountsAsync(
        MailScope scope,
        MailPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{AccountColumns}}
            FROM mail.smtp_accounts
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND (@include_archived OR status = 1)
              AND (@query = ''
                   OR name ILIKE '%' || @query || '%'
                   OR host ILIKE '%' || @query || '%'
                   OR from_address ILIKE '%' || @query || '%')
            ORDER BY lower(name), id
            OFFSET @offset
            LIMIT @limit;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("include_archived", request.IncludeInactive);
        command.Parameters.AddWithValue("query", request.Query);
        command.Parameters.AddWithValue("offset", request.Offset);
        command.Parameters.AddWithValue("limit", request.PageSize + 1);
        var items = new List<SmtpAccount>(request.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadAccount(reader));
        }

        return Trim(items, request.PageSize);
    }

    public async Task<SmtpAccount?> GetAccountAsync(
        MailScope scope,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{AccountColumns}}
            FROM mail.smtp_accounts
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND id = @id;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("id", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAccount(reader) : null;
    }

    public async Task<bool> TryCreateAccountAsync(
        SmtpAccount account,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO mail.smtp_accounts (
                id, tenant_id, application_id, name, host, port, security, username,
                credential_ciphertext, from_address, from_name, status, version,
                created_at, updated_at, archived_at)
            VALUES (
                @id, @tenant_id, @application_id, @name, @host, @port, @security, @username,
                @credential_ciphertext, @from_address, @from_name, @status, @version,
                @created_at, @updated_at, @archived_at)
            ON CONFLICT DO NOTHING;
            """);
        AddAccount(command, account);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateAccountAsync(
        SmtpAccount account,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE mail.smtp_accounts
            SET name = @name,
                host = @host,
                port = @port,
                security = @security,
                username = @username,
                credential_ciphertext = @credential_ciphertext,
                from_address = @from_address,
                from_name = @from_name,
                status = @status,
                version = @version,
                updated_at = @updated_at,
                archived_at = @archived_at
            WHERE id = @id
              AND tenant_id = @tenant_id
              AND application_id = @application_id
              AND version = @expected_version;
            """);
        AddAccount(command, account);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<MailPage<MailDelivery>> ListDeliveriesAsync(
        MailScope scope,
        MailDeliveryStatus? status,
        MailPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $$"""
            SELECT {{DeliveryColumns}}
            FROM mail.deliveries
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND (@status = 0 OR status = @status)
            ORDER BY created_at DESC, id DESC
            OFFSET @offset
            LIMIT @limit;
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("status", (short)(status ?? 0));
        command.Parameters.AddWithValue("offset", request.Offset);
        command.Parameters.AddWithValue("limit", request.PageSize + 1);
        var items = new List<MailDelivery>(request.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadDelivery(reader));
        }

        return Trim(items, request.PageSize);
    }

    public Task<MailDelivery?> GetDeliveryAsync(
        MailScope scope,
        Guid deliveryId,
        CancellationToken cancellationToken) => GetDeliveryCoreAsync(
            scope,
            "id = @lookup",
            deliveryId,
            cancellationToken);

    public Task<MailDelivery?> GetDeliveryByClientMessageIdAsync(
        MailScope scope,
        string clientMessageId,
        CancellationToken cancellationToken) => GetDeliveryCoreAsync(
            scope,
            "client_message_id = @lookup",
            clientMessageId,
            cancellationToken);

    public async Task<bool> TryCreateDeliveryAsync(
        MailDelivery delivery,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO mail.deliveries (
                id, tenant_id, application_id, smtp_account_id, client_message_id,
                recipients_to, recipients_cc, recipients_bcc, reply_to, subject, status,
                provider_message_id, error_code, error_message, created_at, completed_at)
            VALUES (
                @id, @tenant_id, @application_id, @smtp_account_id, @client_message_id,
                @recipients_to, @recipients_cc, @recipients_bcc, @reply_to, @subject, @status,
                @provider_message_id, @error_code, @error_message, @created_at, @completed_at)
            ON CONFLICT DO NOTHING;
            """);
        AddDelivery(command, delivery);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryCompleteDeliveryAsync(
        MailDelivery delivery,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE mail.deliveries
            SET status = @status,
                provider_message_id = @provider_message_id,
                error_code = @error_code,
                error_message = @error_message,
                completed_at = @completed_at
            WHERE id = @id
              AND tenant_id = @tenant_id
              AND application_id = @application_id
              AND status = 1;
            """);
        AddDelivery(command, delivery);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<MailDelivery?> GetDeliveryCoreAsync(
        MailScope scope,
        string predicate,
        object lookup,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {DeliveryColumns}
            FROM mail.deliveries
            WHERE tenant_id = @tenant_id
              AND application_id = @application_id
              AND {predicate};
            """);
        AddScope(command, scope);
        command.Parameters.AddWithValue("lookup", lookup);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDelivery(reader) : null;
    }

    private static void AddScope(NpgsqlCommand command, MailScope scope)
    {
        command.Parameters.AddWithValue("tenant_id", scope.TenantId);
        command.Parameters.AddWithValue("application_id", scope.ApplicationId);
    }

    private static void AddAccount(NpgsqlCommand command, SmtpAccount account)
    {
        command.Parameters.AddWithValue("id", account.Id);
        AddScope(command, account.Scope);
        command.Parameters.AddWithValue("name", account.Name);
        command.Parameters.AddWithValue("host", account.Host);
        command.Parameters.AddWithValue("port", account.Port);
        command.Parameters.AddWithValue("security", (short)account.Security);
        command.Parameters.AddWithValue("username", account.Username);
        command.Parameters.AddWithValue("credential_ciphertext", account.CredentialCiphertext);
        command.Parameters.AddWithValue("from_address", account.FromAddress);
        command.Parameters.AddWithValue("from_name", account.FromName);
        command.Parameters.AddWithValue("status", (short)account.Status);
        command.Parameters.AddWithValue("version", account.Version);
        command.Parameters.AddWithValue("created_at", account.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", account.UpdatedAt.UtcDateTime);
        AddNullableTimestamp(command, "archived_at", account.ArchivedAt);
    }

    private static void AddDelivery(NpgsqlCommand command, MailDelivery delivery)
    {
        command.Parameters.AddWithValue("id", delivery.Id);
        AddScope(command, delivery.Scope);
        command.Parameters.AddWithValue("smtp_account_id", delivery.SmtpAccountId);
        command.Parameters.AddWithValue("client_message_id", delivery.ClientMessageId);
        command.Parameters.AddWithValue("recipients_to", delivery.To.ToArray());
        command.Parameters.AddWithValue("recipients_cc", delivery.Cc.ToArray());
        command.Parameters.AddWithValue("recipients_bcc", delivery.Bcc.ToArray());
        command.Parameters.AddWithValue("reply_to", delivery.ReplyTo);
        command.Parameters.AddWithValue("subject", delivery.Subject);
        command.Parameters.AddWithValue("status", (short)delivery.Status);
        command.Parameters.AddWithValue("provider_message_id", delivery.ProviderMessageId);
        command.Parameters.AddWithValue("error_code", delivery.ErrorCode);
        command.Parameters.AddWithValue("error_message", delivery.ErrorMessage);
        command.Parameters.AddWithValue("created_at", delivery.CreatedAt.UtcDateTime);
        AddNullableTimestamp(command, "completed_at", delivery.CompletedAt);
    }

    private static void AddNullableTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset? value) =>
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
            {
                Value = value is null ? DBNull.Value : value.Value.UtcDateTime,
            });

    private static SmtpAccount ReadAccount(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        new MailScope(reader.GetGuid(1), reader.GetGuid(2)),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt32(5),
        (SmtpSecurityMode)reader.GetInt16(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        (MailAccountStatus)reader.GetInt16(11),
        reader.GetInt64(12),
        ToDateTimeOffset(reader.GetDateTime(13)),
        ToDateTimeOffset(reader.GetDateTime(14)),
        reader.IsDBNull(15) ? null : ToDateTimeOffset(reader.GetDateTime(15)));

    private static MailDelivery ReadDelivery(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        new MailScope(reader.GetGuid(1), reader.GetGuid(2)),
        reader.GetGuid(3),
        reader.GetString(4),
        reader.GetFieldValue<string[]>(5),
        reader.GetFieldValue<string[]>(6),
        reader.GetFieldValue<string[]>(7),
        reader.GetString(8),
        reader.GetString(9),
        (MailDeliveryStatus)reader.GetInt16(10),
        reader.GetString(11),
        reader.GetString(12),
        reader.GetString(13),
        ToDateTimeOffset(reader.GetDateTime(14)),
        reader.IsDBNull(15) ? null : ToDateTimeOffset(reader.GetDateTime(15)));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static MailPage<T> Trim<T>(List<T> items, int pageSize)
    {
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new(items, hasMore);
    }
}
