using Asterloom.Modules.Errors;
using Asterloom.Modules.Mail.Model;
using Asterloom.Modules.Mail.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace Asterloom.Modules.Mail;

public sealed class MailAccountManagementService(
    IMailStore store,
    IDataProtectionProvider dataProtectionProvider,
    MailDeliveryService deliveryService,
    TimeProvider timeProvider)
{
    private readonly IDataProtector _credentialProtector = dataProtectionProvider.CreateProtector(
        "Asterloom.Mail.SmtpCredential.v1");

    public async Task<MailListResult<SmtpAccount>> ListAccountsAsync(
        string tenantId,
        string applicationId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var scope = MailValidation.ParseScope(tenantId, applicationId);
        var request = MailValidation.CreatePageRequest(
            pageSize,
            pageToken,
            query,
            includeArchived);
        var page = await store.ListAccountsAsync(scope, request, cancellationToken);
        return new(
            page.Items,
            page.HasMore ? MailValidation.NextPageToken(request, page.Items.Count) : string.Empty);
    }

    public async Task<SmtpAccount> GetAccountAsync(
        string tenantId,
        string applicationId,
        string smtpAccountId,
        CancellationToken cancellationToken) =>
        await RequireAccountAsync(
            MailValidation.ParseScope(tenantId, applicationId),
            MailValidation.ParseId(smtpAccountId, "smtpAccountId"),
            cancellationToken);

    public async Task<SmtpAccount> CreateAccountAsync(
        string tenantId,
        string applicationId,
        string name,
        string host,
        int port,
        SmtpSecurityMode security,
        string username,
        string smtpPassword,
        string fromAddress,
        string fromName,
        CancellationToken cancellationToken)
    {
        var scope = MailValidation.ParseScope(tenantId, applicationId);
        var now = timeProvider.GetUtcNow();
        var password = MailValidation.RequireText(smtpPassword, "smtpPassword", 1_024);
        var account = new SmtpAccount(
            Guid.CreateVersion7(now),
            scope,
            MailValidation.RequireText(name, "name", 200),
            MailValidation.NormalizeHost(host),
            MailValidation.ValidatePort(port),
            MailValidation.ValidateSecurity(security),
            MailValidation.RequireText(username, "username", 320),
            _credentialProtector.Protect(password),
            MailValidation.NormalizeEmail(fromAddress, "fromAddress"),
            MailValidation.NormalizeOptionalText(fromName, "fromName", 200),
            MailAccountStatus.Active,
            1,
            now,
            now,
            null);
        if (!await store.TryCreateAccountAsync(account, cancellationToken))
        {
            throw new AsterloomException(
                AsterloomErrorKind.AlreadyExists,
                "mail_account_name_exists",
                "An SMTP account with the same name already exists in this application.");
        }

        return account;
    }

    public async Task<SmtpAccount> UpdateAccountAsync(
        string tenantId,
        string applicationId,
        string smtpAccountId,
        string name,
        string host,
        int port,
        SmtpSecurityMode security,
        string username,
        string smtpPassword,
        string fromAddress,
        string fromName,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = MailValidation.ParseScope(tenantId, applicationId);
        var current = await RequireAccountAsync(
            scope,
            MailValidation.ParseId(smtpAccountId, "smtpAccountId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current);
        var ciphertext = string.IsNullOrEmpty(smtpPassword)
            ? current.CredentialCiphertext
            : _credentialProtector.Protect(
                MailValidation.RequireText(smtpPassword, "smtpPassword", 1_024));
        var updated = current with
        {
            Name = MailValidation.RequireText(name, "name", 200),
            Host = MailValidation.NormalizeHost(host),
            Port = MailValidation.ValidatePort(port),
            Security = MailValidation.ValidateSecurity(security),
            Username = MailValidation.RequireText(username, "username", 320),
            CredentialCiphertext = ciphertext,
            FromAddress = MailValidation.NormalizeEmail(fromAddress, "fromAddress"),
            FromName = MailValidation.NormalizeOptionalText(fromName, "fromName", 200),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        await SaveAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    public Task<SmtpAccount> ArchiveAccountAsync(
        string tenantId,
        string applicationId,
        string smtpAccountId,
        long expectedVersion,
        CancellationToken cancellationToken) => ChangeStatusAsync(
            tenantId,
            applicationId,
            smtpAccountId,
            expectedVersion,
            MailAccountStatus.Archived,
            cancellationToken);

    public Task<SmtpAccount> RestoreAccountAsync(
        string tenantId,
        string applicationId,
        string smtpAccountId,
        long expectedVersion,
        CancellationToken cancellationToken) => ChangeStatusAsync(
            tenantId,
            applicationId,
            smtpAccountId,
            expectedVersion,
            MailAccountStatus.Active,
            cancellationToken);

    public Task<MailDelivery> TestAccountAsync(
        string tenantId,
        string applicationId,
        string smtpAccountId,
        string recipient,
        CancellationToken cancellationToken)
    {
        var scope = MailValidation.ParseScope(tenantId, applicationId);
        var draft = new MailMessageDraft(
            scope,
            MailValidation.ParseId(smtpAccountId, "smtpAccountId"),
            $"smtp-test:{Guid.CreateVersion7():N}",
            [recipient],
            [],
            [],
            string.Empty,
            "Asterloom SMTP test",
            "This message confirms that the Asterloom SMTP account can send email.",
            "<p>This message confirms that the <strong>Asterloom SMTP account</strong> can send email.</p>");
        return deliveryService.SendAsync(draft, cancellationToken);
    }

    public async Task<MailListResult<MailDelivery>> ListDeliveriesAsync(
        string tenantId,
        string applicationId,
        int pageSize,
        string? pageToken,
        MailDeliveryStatus? status,
        CancellationToken cancellationToken)
    {
        var scope = MailValidation.ParseScope(tenantId, applicationId);
        var request = MailValidation.CreatePageRequest(
            pageSize,
            pageToken,
            query: null,
            includeInactive: true);
        var page = await store.ListDeliveriesAsync(scope, status, request, cancellationToken);
        return new(
            page.Items,
            page.HasMore ? MailValidation.NextPageToken(request, page.Items.Count) : string.Empty);
    }

    public async Task<MailDelivery> GetDeliveryAsync(
        string tenantId,
        string applicationId,
        string deliveryId,
        CancellationToken cancellationToken)
    {
        var result = await store.GetDeliveryAsync(
            MailValidation.ParseScope(tenantId, applicationId),
            MailValidation.ParseId(deliveryId, "deliveryId"),
            cancellationToken);
        return result ?? throw NotFound("mail_delivery_not_found", "The mail delivery was not found.");
    }

    private async Task<SmtpAccount> ChangeStatusAsync(
        string tenantId,
        string applicationId,
        string smtpAccountId,
        long expectedVersion,
        MailAccountStatus status,
        CancellationToken cancellationToken)
    {
        var current = await RequireAccountAsync(
            MailValidation.ParseScope(tenantId, applicationId),
            MailValidation.ParseId(smtpAccountId, "smtpAccountId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == status)
        {
            return current;
        }

        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = status,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = status == MailAccountStatus.Archived ? now : null,
        };
        await SaveAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    private async Task<SmtpAccount> RequireAccountAsync(
        MailScope scope,
        Guid accountId,
        CancellationToken cancellationToken) =>
        await store.GetAccountAsync(scope, accountId, cancellationToken)
        ?? throw NotFound("mail_account_not_found", "The SMTP account was not found.");

    private async Task SaveAsync(
        SmtpAccount account,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!await store.TryUpdateAccountAsync(account, expectedVersion, cancellationToken))
        {
            throw new AsterloomException(
                AsterloomErrorKind.Conflict,
                "mail_version_conflict",
                "The SMTP account changed. Reload it and retry.");
        }
    }

    private static void RequireVersion(long currentVersion, long expectedVersion)
    {
        if (expectedVersion != currentVersion)
        {
            throw new AsterloomException(
                AsterloomErrorKind.Conflict,
                "mail_version_conflict",
                "The SMTP account changed. Reload it and retry.");
        }
    }

    private static void RequireActive(SmtpAccount account)
    {
        if (account.Status != MailAccountStatus.Active)
        {
            throw new AsterloomException(
                AsterloomErrorKind.FailedPrecondition,
                "mail_account_archived",
                "An archived SMTP account cannot be changed or used for delivery.");
        }
    }

    private static AsterloomException NotFound(string code, string message) => new(
        AsterloomErrorKind.NotFound,
        code,
        message);
}
