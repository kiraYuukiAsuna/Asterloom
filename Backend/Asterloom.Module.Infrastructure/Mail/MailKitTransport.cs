using Asterloom.Modules.Mail.Model;
using Asterloom.Modules.Mail.Transport;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Asterloom.Modules.Infrastructure.Mail;

internal sealed class MailKitTransport : IMailTransport
{
    public async Task<string> SendAsync(
        SmtpTransportAccount account,
        MailTransportMessage message,
        CancellationToken cancellationToken)
    {
        var mimeMessage = CreateMessage(account, message);
        using var client = new SmtpClient { Timeout = 30_000 };
        try
        {
            await client.ConnectAsync(
                account.Host,
                account.Port,
                account.Security == SmtpSecurityMode.SslOnConnect
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls,
                cancellationToken);
            await client.AuthenticateAsync(account.Username, account.Password, cancellationToken);
            await client.SendAsync(mimeMessage, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return mimeMessage.MessageId ?? string.Empty;
        }
        catch (AuthenticationException exception)
        {
            throw new MailTransportException(
                "smtp_authentication_failed",
                "The SMTP server rejected the username or authorization code.",
                exception);
        }
        catch (SslHandshakeException exception)
        {
            throw new MailTransportException(
                "smtp_tls_failed",
                "A secure TLS connection to the SMTP server could not be established.",
                exception);
        }
        catch (SmtpCommandException exception)
        {
            throw new MailTransportException(
                "smtp_command_rejected",
                $"The SMTP server rejected the message ({exception.StatusCode}).",
                exception);
        }
        catch (SmtpProtocolException exception)
        {
            throw new MailTransportException(
                "smtp_protocol_failed",
                "The SMTP server returned an invalid or incomplete response.",
                exception);
        }
        catch (IOException exception)
        {
            throw new MailTransportException(
                "smtp_connection_failed",
                "The connection to the SMTP server failed.",
                exception);
        }
    }

    private static MimeMessage CreateMessage(
        SmtpTransportAccount account,
        MailTransportMessage message)
    {
        var result = new MimeMessage();
        result.From.Add(new MailboxAddress(account.FromName, account.FromAddress));
        result.To.AddRange(message.To.Select(MailboxAddress.Parse));
        result.Cc.AddRange(message.Cc.Select(MailboxAddress.Parse));
        result.Bcc.AddRange(message.Bcc.Select(MailboxAddress.Parse));
        if (message.ReplyTo.Length > 0)
        {
            result.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
        }

        result.Subject = message.Subject;
        var body = new BodyBuilder();
        if (message.TextBody.Length > 0)
        {
            body.TextBody = message.TextBody;
        }
        if (message.HtmlBody.Length > 0)
        {
            body.HtmlBody = message.HtmlBody;
        }
        result.Body = body.ToMessageBody();
        return result;
    }
}
