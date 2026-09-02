using Asterloom.Modules.Mail.Model;
using Google.Protobuf.WellKnownTypes;
using ProtocolAccountStatus = Asterloom.Protocol.Mail.V1.MailAccountStatus;
using ProtocolDelivery = Asterloom.Protocol.Mail.V1.MailDelivery;
using ProtocolDeliveryStatus = Asterloom.Protocol.Mail.V1.MailDeliveryStatus;
using ProtocolScope = Asterloom.Protocol.Mail.V1.MailScope;
using ProtocolSecurity = Asterloom.Protocol.Mail.V1.SmtpSecurity;
using ProtocolSmtpAccount = Asterloom.Protocol.Mail.V1.SmtpAccount;

namespace Asterloom.Modules.Mail;

internal static class MailProtocolMapper
{
    public static ProtocolSmtpAccount ToProtocol(this SmtpAccount account) => new()
    {
        Id = account.Id.ToString("D"),
        Scope = account.Scope.ToProtocol(),
        Name = account.Name,
        Host = account.Host,
        Port = account.Port,
        Security = account.Security.ToProtocol(),
        Username = account.Username,
        FromAddress = account.FromAddress,
        FromName = account.FromName,
        Status = account.Status.ToProtocol(),
        Version = account.Version,
        CreatedAt = account.CreatedAt.ToTimestamp(),
        UpdatedAt = account.UpdatedAt.ToTimestamp(),
        ArchivedAt = account.ArchivedAt?.ToTimestamp(),
    };

    public static ProtocolDelivery ToProtocol(this MailDelivery delivery)
    {
        var result = new ProtocolDelivery
        {
            Id = delivery.Id.ToString("D"),
            Scope = delivery.Scope.ToProtocol(),
            SmtpAccountId = delivery.SmtpAccountId.ToString("D"),
            ClientMessageId = delivery.ClientMessageId,
            ReplyTo = delivery.ReplyTo,
            Subject = delivery.Subject,
            Status = delivery.Status.ToProtocol(),
            ProviderMessageId = delivery.ProviderMessageId,
            ErrorCode = delivery.ErrorCode,
            ErrorMessage = delivery.ErrorMessage,
            CreatedAt = delivery.CreatedAt.ToTimestamp(),
            CompletedAt = delivery.CompletedAt?.ToTimestamp(),
        };
        result.To.AddRange(delivery.To);
        result.Cc.AddRange(delivery.Cc);
        result.Bcc.AddRange(delivery.Bcc);
        return result;
    }

    public static SmtpSecurityMode ToModel(this ProtocolSecurity security) => security switch
    {
        ProtocolSecurity.StartTls => SmtpSecurityMode.StartTls,
        ProtocolSecurity.SslOnConnect => SmtpSecurityMode.SslOnConnect,
        _ => (SmtpSecurityMode)0,
    };

    public static MailDeliveryStatus? ToOptionalModel(
        this ProtocolDeliveryStatus status) => status switch
    {
        ProtocolDeliveryStatus.Pending => MailDeliveryStatus.Pending,
        ProtocolDeliveryStatus.Sent => MailDeliveryStatus.Sent,
        ProtocolDeliveryStatus.Failed => MailDeliveryStatus.Failed,
        _ => null,
    };

    private static ProtocolScope ToProtocol(this MailScope scope) => new()
    {
        TenantId = scope.TenantId.ToString("D"),
        ApplicationId = scope.ApplicationId.ToString("D"),
    };

    private static ProtocolSecurity ToProtocol(this SmtpSecurityMode security) => security switch
    {
        SmtpSecurityMode.StartTls => ProtocolSecurity.StartTls,
        SmtpSecurityMode.SslOnConnect => ProtocolSecurity.SslOnConnect,
        _ => ProtocolSecurity.Unspecified,
    };

    private static ProtocolAccountStatus ToProtocol(this MailAccountStatus status) => status switch
    {
        MailAccountStatus.Active => ProtocolAccountStatus.Active,
        MailAccountStatus.Archived => ProtocolAccountStatus.Archived,
        _ => ProtocolAccountStatus.Unspecified,
    };

    private static ProtocolDeliveryStatus ToProtocol(this MailDeliveryStatus status) => status switch
    {
        MailDeliveryStatus.Pending => ProtocolDeliveryStatus.Pending,
        MailDeliveryStatus.Sent => ProtocolDeliveryStatus.Sent,
        MailDeliveryStatus.Failed => ProtocolDeliveryStatus.Failed,
        _ => ProtocolDeliveryStatus.Unspecified,
    };
}
