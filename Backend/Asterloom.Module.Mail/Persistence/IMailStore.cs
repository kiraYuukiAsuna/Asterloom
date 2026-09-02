using Asterloom.Modules.Mail.Model;

namespace Asterloom.Modules.Mail.Persistence;

public interface IMailStore
{
    Task<MailPage<SmtpAccount>> ListAccountsAsync(
        MailScope scope,
        MailPageRequest request,
        CancellationToken cancellationToken);

    Task<SmtpAccount?> GetAccountAsync(
        MailScope scope,
        Guid accountId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateAccountAsync(
        SmtpAccount account,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateAccountAsync(
        SmtpAccount account,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<MailPage<MailDelivery>> ListDeliveriesAsync(
        MailScope scope,
        MailDeliveryStatus? status,
        MailPageRequest request,
        CancellationToken cancellationToken);

    Task<MailDelivery?> GetDeliveryAsync(
        MailScope scope,
        Guid deliveryId,
        CancellationToken cancellationToken);

    Task<MailDelivery?> GetDeliveryByClientMessageIdAsync(
        MailScope scope,
        string clientMessageId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateDeliveryAsync(
        MailDelivery delivery,
        CancellationToken cancellationToken);

    Task<bool> TryCompleteDeliveryAsync(
        MailDelivery delivery,
        CancellationToken cancellationToken);
}
