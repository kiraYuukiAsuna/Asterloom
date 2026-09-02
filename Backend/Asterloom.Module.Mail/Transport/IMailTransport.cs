using Asterloom.Modules.Mail.Model;

namespace Asterloom.Modules.Mail.Transport;

public interface IMailTransport
{
    Task<string> SendAsync(
        SmtpTransportAccount account,
        MailTransportMessage message,
        CancellationToken cancellationToken);
}

public sealed record SmtpTransportAccount(
    string Host,
    int Port,
    SmtpSecurityMode Security,
    string Username,
    string Password,
    string FromAddress,
    string FromName);

public sealed record MailTransportMessage(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string ReplyTo,
    string Subject,
    string TextBody,
    string HtmlBody);

public sealed class MailTransportException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
