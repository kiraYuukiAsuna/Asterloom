using System.Security.Cryptography;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Mail.Model;
using Asterloom.Modules.Mail.Persistence;
using Asterloom.Modules.Mail.Transport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Asterloom.Modules.Mail;

public sealed class MailDeliveryService(
    IMailStore store,
    IMailTransport transport,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider,
    ILogger<MailDeliveryService> logger)
{
    private static readonly Action<ILogger, Guid, Exception?> LogCredentialFailure =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(2101, nameof(LogCredentialFailure)),
            "Could not decrypt SMTP credentials for account {AccountId}.");

    private static readonly Action<ILogger, Guid, Guid, Exception?> LogTransportFailure =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(2102, nameof(LogTransportFailure)),
            "SMTP delivery {DeliveryId} failed through account {AccountId}.");

    private static readonly Action<ILogger, Guid, Guid, Exception?> LogUnexpectedFailure =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Error,
            new EventId(2103, nameof(LogUnexpectedFailure)),
            "Unexpected SMTP delivery failure for {DeliveryId} through account {AccountId}.");

    private readonly IDataProtector _credentialProtector = dataProtectionProvider.CreateProtector(
        "Asterloom.Mail.SmtpCredential.v1");

    public async Task<MailDelivery> SendAsync(
        MailMessageDraft input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var draft = Normalize(input);
        var existing = await store.GetDeliveryByClientMessageIdAsync(
            draft.Scope,
            draft.ClientMessageId,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var account = await store.GetAccountAsync(
            draft.Scope,
            draft.SmtpAccountId,
            cancellationToken)
            ?? throw new AsterloomException(
                AsterloomErrorKind.NotFound,
                "mail_account_not_found",
                "The SMTP account was not found.");
        if (account.Status != MailAccountStatus.Active)
        {
            throw new AsterloomException(
                AsterloomErrorKind.FailedPrecondition,
                "mail_account_archived",
                "The SMTP account is archived and cannot send email.");
        }

        var now = timeProvider.GetUtcNow();
        var delivery = new MailDelivery(
            Guid.CreateVersion7(now),
            draft.Scope,
            account.Id,
            draft.ClientMessageId,
            draft.To,
            draft.Cc,
            draft.Bcc,
            draft.ReplyTo,
            draft.Subject,
            MailDeliveryStatus.Pending,
            string.Empty,
            string.Empty,
            string.Empty,
            now,
            null);
        if (!await store.TryCreateDeliveryAsync(delivery, cancellationToken))
        {
            return await store.GetDeliveryByClientMessageIdAsync(
                draft.Scope,
                draft.ClientMessageId,
                cancellationToken)
                ?? throw new AsterloomException(
                    AsterloomErrorKind.Conflict,
                    "mail_delivery_conflict",
                    "The delivery could not be reserved. Retry with the same client message ID.");
        }

        try
        {
            var password = _credentialProtector.Unprotect(account.CredentialCiphertext);
            var providerMessageId = await transport.SendAsync(
                new SmtpTransportAccount(
                    account.Host,
                    account.Port,
                    account.Security,
                    account.Username,
                    password,
                    account.FromAddress,
                    account.FromName),
                new MailTransportMessage(
                    draft.To,
                    draft.Cc,
                    draft.Bcc,
                    draft.ReplyTo,
                    draft.Subject,
                    draft.TextBody,
                    draft.HtmlBody),
                cancellationToken);
            delivery = delivery with
            {
                Status = MailDeliveryStatus.Sent,
                ProviderMessageId = providerMessageId,
                CompletedAt = timeProvider.GetUtcNow(),
            };
        }
        catch (CryptographicException exception)
        {
            LogCredentialFailure(logger, account.Id, exception);
            delivery = Failed(
                delivery,
                "mail_credential_unavailable",
                "The SMTP credential cannot be decrypted. Re-enter the account authorization code.");
        }
        catch (MailTransportException exception)
        {
            LogTransportFailure(logger, delivery.Id, account.Id, exception);
            delivery = Failed(delivery, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUnexpectedFailure(logger, delivery.Id, account.Id, exception);
            delivery = Failed(
                delivery,
                "smtp_delivery_failed",
                "The SMTP server could not accept the message.");
        }

        if (!await store.TryCompleteDeliveryAsync(delivery, CancellationToken.None))
        {
            throw new AsterloomException(
                AsterloomErrorKind.Conflict,
                "mail_delivery_completion_conflict",
                "The delivery result could not be recorded.");
        }

        return delivery;
    }

    private MailDelivery Failed(MailDelivery delivery, string errorCode, string errorMessage) =>
        delivery with
        {
            Status = MailDeliveryStatus.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage.Length <= 1_000
                ? errorMessage
                : errorMessage[..1_000],
            CompletedAt = timeProvider.GetUtcNow(),
        };

    private static MailMessageDraft Normalize(MailMessageDraft input)
    {
        if (input.Scope.TenantId == Guid.Empty || input.Scope.ApplicationId == Guid.Empty)
        {
            throw MailValidation.Invalid("scope", "A tenant and application scope is required.");
        }
        if (input.SmtpAccountId == Guid.Empty)
        {
            throw MailValidation.Invalid("smtpAccountId", "smtpAccountId cannot be empty.");
        }

        var to = MailValidation.NormalizeRecipients(input.To, "to");
        var cc = MailValidation.NormalizeRecipients(input.Cc, "cc");
        var bcc = MailValidation.NormalizeRecipients(input.Bcc, "bcc");
        if (to.Count == 0)
        {
            throw MailValidation.Invalid("to", "At least one To recipient is required.");
        }
        if (to.Count + cc.Count + bcc.Count > 100)
        {
            throw MailValidation.Invalid("to", "A message cannot contain more than 100 recipients.");
        }

        var textBody = MailValidation.NormalizeBody(input.TextBody, "textBody");
        var htmlBody = MailValidation.NormalizeBody(input.HtmlBody, "htmlBody");
        if (textBody.Length == 0 && htmlBody.Length == 0)
        {
            throw MailValidation.Invalid("textBody", "A text or HTML body is required.");
        }

        return input with
        {
            ClientMessageId = MailValidation.NormalizeClientMessageId(input.ClientMessageId),
            To = to,
            Cc = cc,
            Bcc = bcc,
            ReplyTo = string.IsNullOrWhiteSpace(input.ReplyTo)
                ? string.Empty
                : MailValidation.NormalizeEmail(input.ReplyTo, "replyTo"),
            Subject = MailValidation.NormalizeSubject(input.Subject),
            TextBody = textBody,
            HtmlBody = htmlBody,
        };
    }
}
